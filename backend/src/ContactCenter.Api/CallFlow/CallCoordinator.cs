using System.Threading.Channels;
using ContactCenter.Api.Agents;
using ContactCenter.Api.Ari;

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

    private readonly IAriClient _ari;
    private readonly AgentStateService _agents;
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

    public CallCoordinator(IAriClient ari, AgentStateService agents, ILogger<CallCoordinator> logger)
    {
        _ari = ari;
        _agents = agents;
        _logger = logger;
        _agents.RequestDispatch = SignalDispatch;
    }

    private sealed record WaitingCaller(string ChannelId, string QueueName, string CallerId);

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
        await _gate.WaitAsync(ct);
        try
        {
            var holdingBridge = await GetOrCreateHoldingBridgeAsync(queueName, ct);
            await _ari.AddToBridgeAsync(holdingBridge, callerChannelId, ct);
            _waiting.Add(new WaitingCaller(callerChannelId, queueName, callerId));
            _logger.LogInformation("Beller {Channel} in de wacht voor '{Queue}' ({Count} wachtend)",
                callerChannelId, queueName, _waiting.Count(w => w.QueueName == queueName));
        }
        finally
        {
            _gate.Release();
        }
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
        }
        finally
        {
            _gate.Release();
        }

        if (wrapUpAgent is not null)
            await _agents.BeginWrapUpAsync(wrapUpAgent, ct);
        if (releaseAgent is not null)
            await _agents.ReleaseReservationAsync(releaseAgent, ct);
        if (requeue is not null)
            SignalDispatch();
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

                    progress = true;
                    break; // wachtlijst is gemuteerd; herstart de pass
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Hulpfuncties ---------------------------------------------------------

    private async Task<string> GetOrCreateHoldingBridgeAsync(string queueName, CancellationToken ct)
    {
        if (_holdingBridges.TryGetValue(queueName, out var existing))
            return existing;

        var bridgeId = await _ari.CreateBridgeAsync("holding", ct);
        await _ari.StartBridgeMohAsync(bridgeId, MohQueueWaiting, ct);
        _holdingBridges[queueName] = bridgeId;
        _logger.LogInformation("Holding-brug {Bridge} aangemaakt voor wachtrij '{Queue}'", bridgeId, queueName);
        return bridgeId;
    }

    private PendingConnect? FindPendingByCaller(string callerChannelId)
        => _pending.Values.FirstOrDefault(p => p.Caller.ChannelId == callerChannelId);

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
