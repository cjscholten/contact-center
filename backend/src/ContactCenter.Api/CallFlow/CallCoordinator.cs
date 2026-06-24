using System.Threading.Channels;
using ContactCenter.Api.Agents;
using ContactCenter.Api.Ari;
using ContactCenter.Api.Data;
using ContactCenter.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.CallFlow;

/// <summary>
/// Bestuurt de gesprekken server-side via ARI-bruggen. Wachtende bellers staan in een
/// holding-brug (wachtmuziek) per wachtrij; zodra een agent beschikbaar is wordt die gebeld
/// (originate) en bij opnemen komen beller en agent samen in een mixing-brug. Hold en
/// doorverbinden (volgende deelfases) bouwen op dezelfde bruggen voort.
/// </summary>
public sealed class CallCoordinator : IHostedService
{
    private const string MohQueueWaiting = "default";

    private const string ForwardContext = "cc-forward";

    private readonly IAriClient _ari;
    private readonly AgentStateService _agents;
    private readonly IDbContextFactory<CcDbContext> _dbFactory;
    private readonly IRealtimeNotifier _notifier;
    private readonly ILogger<CallCoordinator> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _holdingBridges = new(StringComparer.Ordinal); // queue → bridgeId
    private readonly List<WaitingCaller> _waiting = [];
    private readonly Dictionary<string, PendingConnect> _pending = new(StringComparer.Ordinal); // agentChannelId → pending
    private readonly Dictionary<string, ActiveCall> _activeByChannel = new(StringComparer.Ordinal);

    private readonly Channel<byte> _dispatchSignals =
        Channel.CreateBounded<byte>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    public CallCoordinator(IAriClient ari, AgentStateService agents,
        IDbContextFactory<CcDbContext> dbFactory, IRealtimeNotifier notifier, ILogger<CallCoordinator> logger)
    {
        _ari = ari;
        _agents = agents;
        _dbFactory = dbFactory;
        _notifier = notifier;
        _logger = logger;
        _agents.RequestDispatch = SignalDispatch;
    }

    private sealed record WaitingCaller(string ChannelId, string QueueName, string CallerId, DateTimeOffset EnqueuedAt);

    private sealed record PendingConnect(string AgentName, string AgentChannelId, WaitingCaller Caller);

    private sealed class ActiveCall
    {
        public required string CallerChannelId { get; init; }
        public required string AgentChannelId { get; init; }
        public required string AgentName { get; init; }
        public required string QueueName { get; init; }
        public required string CallerId { get; init; }
        public required string MixingBridgeId { get; init; }
        public bool OnHold { get; set; }
    }

    // --- Inkomende beller -----------------------------------------------------

    /// <summary>Plaatst een beantwoorde beller in de wacht (holding-brug + wachtmuziek) en triggert dispatch.</summary>
    public async Task EnqueueCallerAsync(string callerChannelId, string queueName, string callerId,
        CancellationToken ct = default)
    {
        List<WaitingCallView> snapshot;
        await _gate.WaitAsync(ct);
        try
        {
            await PlaceInHoldingAsync(queueName, callerChannelId, ct);
            _waiting.Add(new WaitingCaller(callerChannelId, queueName, callerId, DateTimeOffset.UtcNow));
            _logger.LogInformation("Beller {Channel} in de wacht voor '{Queue}' ({Count} wachtend)",
                callerChannelId, queueName, _waiting.Count(w => w.QueueName == queueName));
            snapshot = BuildWaitingView();
        }
        finally
        {
            _gate.Release();
        }
        await NotifyQueuesAsync(snapshot, ct);
        SignalDispatch();
    }

    // --- Agent neemt op -------------------------------------------------------

    public async Task OnAgentAnsweredAsync(string agentChannelId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_pending.Remove(agentChannelId, out var pending))
            {
                _logger.LogWarning("Agent-kanaal {Channel} nam op maar er is geen wachtende reservering", agentChannelId);
                await SafeHangupAsync(agentChannelId, ct);
                return;
            }

            var mixingBridge = await _ari.CreateBridgeAsync("mixing", ct);
            await _ari.AddToBridgeAsync(mixingBridge, pending.Caller.ChannelId, ct); // verplaatst uit holding
            await _ari.AddToBridgeAsync(mixingBridge, agentChannelId, ct);

            var call = new ActiveCall
            {
                CallerChannelId = pending.Caller.ChannelId,
                AgentChannelId = agentChannelId,
                AgentName = pending.AgentName,
                QueueName = pending.Caller.QueueName,
                CallerId = pending.Caller.CallerId,
                MixingBridgeId = mixingBridge,
            };
            _activeByChannel[call.CallerChannelId] = call;
            _activeByChannel[agentChannelId] = call;
            _logger.LogInformation("Agent {Agent} verbonden met beller {Caller} (brug {Bridge})",
                pending.AgentName, pending.Caller.ChannelId, mixingBridge);
        }
        finally
        {
            _gate.Release();
        }

        if (_activeByChannel.TryGetValue(agentChannelId, out var connected))
            await _agents.ConfirmOnCallAsync(connected.AgentName, ct);
    }

    // --- Kanaal weg (ophangen) ------------------------------------------------

    public async Task OnChannelGoneAsync(string channelId, CancellationToken ct = default)
    {
        string? wrapUpAgent = null;
        string? releaseAgent = null;
        WaitingCaller? requeue = null;
        List<WaitingCallView> snapshot;
        await _gate.WaitAsync(ct);
        try
        {
            // 1) actief gesprek: ruim de brug op, hang de andere leg op, agent → nawerktijd
            if (_activeByChannel.TryGetValue(channelId, out var call))
            {
                _activeByChannel.Remove(call.CallerChannelId);
                _activeByChannel.Remove(call.AgentChannelId);
                var other = channelId == call.CallerChannelId ? call.AgentChannelId : call.CallerChannelId;
                await SafeHangupAsync(other, ct);
                await SafeDestroyBridgeAsync(call.MixingBridgeId, ct);
                wrapUpAgent = call.AgentName;
            }
            // 2) wachtende beller hing op vóór verbinding
            else if (_waiting.RemoveAll(w => w.ChannelId == channelId) > 0)
            {
                _logger.LogInformation("Wachtende beller {Channel} hing op", channelId);
            }
            // 3) agent-leg verdween tijdens rinkelen (niet opgenomen): beller terug in de wacht
            else if (_pending.Remove(channelId, out var pending))
            {
                releaseAgent = pending.AgentName;
                requeue = pending.Caller;
            }
            // 4) een beller voor wie een agent aan het rinkelen is, hing zelf op
            else if (FindPendingByCaller(channelId) is { } byCaller)
            {
                _pending.Remove(byCaller.AgentChannelId);
                await SafeHangupAsync(byCaller.AgentChannelId, ct);
                releaseAgent = byCaller.AgentName;
            }

            if (requeue is not null)
            {
                _waiting.Add(requeue); // beller zit nog in de holding-brug, alleen weer kiesbaar maken
                _logger.LogInformation("Agent nam niet op; beller {Channel} terug in de wacht", requeue.ChannelId);
            }
            snapshot = BuildWaitingView();
        }
        finally
        {
            _gate.Release();
        }

        await NotifyQueuesAsync(snapshot, ct);
        if (wrapUpAgent is not null)
            await _agents.BeginWrapUpAsync(wrapUpAgent, ct);
        if (releaseAgent is not null)
            await _agents.ReleaseReservationAsync(releaseAgent, ct);
        if (requeue is not null)
            SignalDispatch();
    }

    // --- In de wacht ----------------------------------------------------------

    /// <summary>Zet de beller in de wacht: van de mixing-brug naar de holding-brug (wachtmuziek).</summary>
    public Task<bool> HoldAsync(string agentName, CancellationToken ct = default)
        => SetHoldAsync(agentName, hold: true, ct);

    /// <summary>Haalt de beller uit de wacht: terug van de holding-brug naar de mixing-brug.</summary>
    public Task<bool> UnholdAsync(string agentName, CancellationToken ct = default)
        => SetHoldAsync(agentName, hold: false, ct);

    private async Task<bool> SetHoldAsync(string agentName, bool hold, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var call = _activeByChannel.Values.FirstOrDefault(c => c.AgentName == agentName);
            if (call is null || call.OnHold == hold)
                return false;

            if (hold)
            {
                await PlaceInHoldingAsync(call.QueueName, call.CallerChannelId, ct); // verplaatst uit de mixing-brug
            }
            else
            {
                await _ari.AddToBridgeAsync(call.MixingBridgeId, call.CallerChannelId, ct); // terug uit de holding-brug
            }

            call.OnHold = hold;
            _logger.LogInformation("Beller {Caller} {Actie} door agent {Agent}",
                call.CallerChannelId, hold ? "in de wacht gezet" : "uit de wacht gehaald", agentName);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Koud doorverbinden ---------------------------------------------------

    /// <summary>
    /// Koud doorverbinden: de agent verlaat het gesprek (→ nawerktijd) en de beller wordt
    /// herrouteerd. Doel = bestaande wachtrijnaam → terug in de wacht + dispatch; anders een
    /// extern nummer → via het cc-forward-dialplan (vereist een uitgaande SBC-route).
    /// </summary>
    public async Task<bool> ColdTransferAsync(string agentName, string target, CancellationToken ct = default)
    {
        var toQueue = await IsQueueAsync(target, ct);

        string callerChannelId, callerId, agentChannelId, mixingBridgeId;
        await _gate.WaitAsync(ct);
        try
        {
            var call = _activeByChannel.Values.FirstOrDefault(c => c.AgentName == agentName);
            if (call is null)
                return false;
            // ontkoppel zodat het ophangen van de agent-leg de beller niet meeneemt
            _activeByChannel.Remove(call.CallerChannelId);
            _activeByChannel.Remove(call.AgentChannelId);
            (callerChannelId, callerId, agentChannelId, mixingBridgeId) =
                (call.CallerChannelId, call.CallerId, call.AgentChannelId, call.MixingBridgeId);
        }
        finally
        {
            _gate.Release();
        }

        await SafeHangupAsync(agentChannelId, ct); // agent klaar met dit gesprek
        if (toQueue)
            await EnqueueCallerAsync(callerChannelId, target, callerId, ct); // verplaatst beller naar holding van doelwachtrij
        else
            await _ari.ContinueInDialplanAsync(callerChannelId, ForwardContext, target, 1, ct);
        await SafeDestroyBridgeAsync(mixingBridgeId, ct); // mixing-brug is nu leeg
        await _agents.BeginWrapUpAsync(agentName, ct);

        _logger.LogInformation("Koud doorverbonden: beller {Caller} → '{Target}' ({Type}); agent {Agent} naar nawerktijd",
            callerChannelId, target, toQueue ? "wachtrij" : "extern", agentName);
        return true;
    }

    /// <summary>
    /// Koud doorverbinden naar een specifieke collega-agent: de huidige agent stapt eruit
    /// (→ nawerktijd), de beller wacht in de holding-brug en de doel-agent wordt gebeld; bij
    /// opnemen volgt de mixing-brug. Faalt als de doel-agent offline/bezet is.
    /// </summary>
    public async Task<bool> TransferToAgentAsync(string fromAgent, string toAgentName, CancellationToken ct = default)
    {
        List<WaitingCallView>? requeueSnapshot = null;
        string? wrapUpAgent = null;
        var ok = false;
        await _gate.WaitAsync(ct);
        try
        {
            var call = _activeByChannel.Values.FirstOrDefault(c => c.AgentName == fromAgent);
            if (call is null)
                return false;
            var reserved = await _agents.TryReserveSpecificAsync(toAgentName, ct);
            if (reserved is null)
                return false; // doel-agent offline of al in gesprek

            _activeByChannel.Remove(call.CallerChannelId);
            _activeByChannel.Remove(call.AgentChannelId);

            await PlaceInHoldingAsync(call.QueueName, call.CallerChannelId, ct); // beller wacht (wachtmuziek)
            await SafeHangupAsync(call.AgentChannelId, ct);
            await SafeDestroyBridgeAsync(call.MixingBridgeId, ct);
            wrapUpAgent = call.AgentName;

            var waiting = new WaitingCaller(call.CallerChannelId, call.QueueName, call.CallerId, DateTimeOffset.UtcNow);
            try
            {
                var toChannel = await _ari.OriginateToStasisAsync(reserved.Endpoint, "agent", call.CallerId, ct: ct);
                _pending[toChannel] = new PendingConnect(reserved.Name, toChannel, waiting);
                _logger.LogInformation("Beller {Caller} doorverbonden van {From} naar agent {To}",
                    call.CallerChannelId, fromAgent, reserved.Name);
                ok = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Originate naar doel-agent {Agent} mislukt; beller terug in de wacht", reserved.Name);
                await _agents.ReleaseReservationAsync(reserved.Name, ct);
                _waiting.Add(waiting);
                requeueSnapshot = BuildWaitingView();
            }
        }
        finally
        {
            _gate.Release();
        }

        if (wrapUpAgent is not null)
            await _agents.BeginWrapUpAsync(wrapUpAgent, ct);
        if (requeueSnapshot is not null)
            await NotifyQueuesAsync(requeueSnapshot, ct);
        return ok;
    }

    private async Task<bool> IsQueueAsync(string target, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.Queues.AsNoTracking().AnyAsync(q => q.Name == target, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Kon doeltype voor '{Target}' niet bepalen ({Reden}); behandel als extern nummer",
                target, ex.Message);
            return false;
        }
    }

    // --- Handmatig aannemen ---------------------------------------------------

    /// <summary>
    /// Een agent pakt zelf een specifiek wachtend gesprek. Race-veilig: claimt het gesprek
    /// alleen als het nog wacht en reserveert déze agent. De agent wordt gebeld (originate);
    /// bij opnemen volgt de mixing-brug, net als bij automatische toewijzing.
    /// </summary>
    public async Task<bool> PickupAsync(string agentName, string callerChannelId, CancellationToken ct = default)
    {
        List<WaitingCallView>? snapshot = null;
        await _gate.WaitAsync(ct);
        try
        {
            var idx = _waiting.FindIndex(w => w.ChannelId == callerChannelId);
            if (idx < 0)
                return false; // gesprek al aangenomen of opgehangen

            var reserved = await _agents.TryReserveSpecificAsync(agentName, ct);
            if (reserved is null)
                return false; // agent heeft al een lopend gesprek

            var caller = _waiting[idx];
            try
            {
                var agentChannelId =
                    await _ari.OriginateToStasisAsync(reserved.Endpoint, "agent", caller.CallerId, ct: ct);
                _waiting.RemoveAt(idx);
                _pending[agentChannelId] = new PendingConnect(reserved.Name, agentChannelId, caller);
                _logger.LogInformation("Beller {Caller} handmatig aangenomen door agent {Agent}, originate {Channel}",
                    caller.ChannelId, reserved.Name, agentChannelId);
                snapshot = BuildWaitingView();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Originate (pickup) naar agent {Agent} mislukt; reservering vrijgeven", reserved.Name);
                await _agents.ReleaseReservationAsync(reserved.Name, ct);
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (snapshot is not null)
            await NotifyQueuesAsync(snapshot, ct);
        return true;
    }

    // --- Dispatch-pomp --------------------------------------------------------

    private void SignalDispatch() => _dispatchSignals.Writer.TryWrite(0);

    private async Task DispatchPumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _dispatchSignals.Reader.ReadAllAsync(ct))
                await TryDispatchAllAsync(ct);
        }
        catch (OperationCanceledException) { /* afsluiten */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dispatch-pomp gestopt door onverwachte fout");
        }
    }

    internal async Task TryDispatchAllAsync(CancellationToken ct = default)
    {
        var anyDispatched = false;
        List<WaitingCallView> snapshot;
        await _gate.WaitAsync(ct);
        try
        {
            var progress = true;
            while (progress)
            {
                progress = false;
                foreach (var caller in _waiting.OrderBy(_ => 0).ToList()) // oudste eerst (invoegvolgorde)
                {
                    var reserved = await _agents.TryReserveForCallAsync([caller.QueueName], ct);
                    if (reserved is null)
                        continue;

                    try
                    {
                        var agentChannelId = await _ari.OriginateToStasisAsync(
                            reserved.Endpoint, "agent", caller.CallerId, ct: ct);
                        _waiting.Remove(caller);
                        _pending[agentChannelId] = new PendingConnect(reserved.Name, agentChannelId, caller);
                        _logger.LogInformation("Beller {Caller} toegewezen aan agent {Agent}, originate {Channel}",
                            caller.ChannelId, reserved.Name, agentChannelId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Originate naar agent {Agent} mislukt; reservering vrijgeven", reserved.Name);
                        await _agents.ReleaseReservationAsync(reserved.Name, ct);
                        continue;
                    }

                    anyDispatched = true;
                    progress = true;
                    break; // wachtlijst is gemuteerd; herstart de pass
                }
            }
            snapshot = BuildWaitingView();
        }
        finally
        {
            _gate.Release();
        }

        if (anyDispatched)
            await NotifyQueuesAsync(snapshot, ct);
    }

    // --- Hulpfuncties ---------------------------------------------------------

    /// <summary>
    /// Plaatst een kanaal in de holding-brug van de wachtrij en (her)start de wachtmuziek.
    /// De MoH moet ná de join opnieuw worden gestart: Asterisk stopt de music-on-hold zodra
    /// de brug leeg raakt (bv. nadat een wachtende beller naar een agent is verbonden).
    /// </summary>
    private async Task PlaceInHoldingAsync(string queueName, string channelId, CancellationToken ct)
    {
        if (!_holdingBridges.TryGetValue(queueName, out var bridgeId))
        {
            bridgeId = await _ari.CreateBridgeAsync("holding", ct);
            _holdingBridges[queueName] = bridgeId;
            _logger.LogInformation("Holding-brug {Bridge} aangemaakt voor wachtrij '{Queue}'", bridgeId, queueName);
        }

        await _ari.AddToBridgeAsync(bridgeId, channelId, ct);
        await _ari.StartBridgeMohAsync(bridgeId, MohQueueWaiting, ct);
    }

    private PendingConnect? FindPendingByCaller(string callerChannelId)
        => _pending.Values.FirstOrDefault(p => p.Caller.ChannelId == callerChannelId);

    /// <summary>Huidige wachtende gesprekken (voor de initiële GET; updates lopen via de notifier).</summary>
    public async Task<IReadOnlyList<WaitingCallView>> GetWaitingViewAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return BuildWaitingView(); }
        finally { _gate.Release(); }
    }

    /// <summary>Aanname: aangeroepen terwijl _gate vastgehouden wordt.</summary>
    private List<WaitingCallView> BuildWaitingView()
        => [.. _waiting.Select(w => new WaitingCallView(w.ChannelId, w.QueueName, w.CallerId, w.EnqueuedAt))];

    private async Task NotifyQueuesAsync(IReadOnlyList<WaitingCallView> snapshot, CancellationToken ct)
    {
        try { await _notifier.QueuesChangedAsync(snapshot, ct); }
        catch (Exception ex) { _logger.LogWarning("Wachtrij-update pushen mislukt: {Reden}", ex.Message); }
    }

    private async Task SafeHangupAsync(string channelId, CancellationToken ct)
    {
        try { await _ari.HangupAsync(channelId, ct); }
        catch (Exception ex) { _logger.LogDebug("Ophangen {Channel} faalde (waarschijnlijk al weg): {Reden}", channelId, ex.Message); }
    }

    private async Task SafeDestroyBridgeAsync(string bridgeId, CancellationToken ct)
    {
        try { await _ari.DestroyBridgeAsync(bridgeId, ct); }
        catch (Exception ex) { _logger.LogDebug("Brug {Bridge} opruimen faalde: {Reden}", bridgeId, ex.Message); }
    }

    // --- Levenscyclus ---------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pumpTask = DispatchPumpAsync(_pumpCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _dispatchSignals.Writer.TryComplete();
        if (_pumpCts is not null)
            await _pumpCts.CancelAsync();
        if (_pumpTask is not null)
            await Task.WhenAny(_pumpTask, Task.Delay(Timeout.Infinite, cancellationToken));
    }
}
