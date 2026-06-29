using System.Collections.Concurrent;
using System.Threading.Channels;
using ContactCenter.Api.Agents;
using ContactCenter.Api.Ari;
using ContactCenter.Api.Data;
using ContactCenter.Api.Realtime;
using ContactCenter.Api.Tts;
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
    private const string DefaultMohClass = "default";

    private const string ForwardContext = "cc-forward";

    // Positie-meldingen: per positienummer één (gecachet) TTS-bestand, afgespeeld op het
    // beller-kanaal in de holding-brug (onderbreekt de wachtmuziek kort, hervat daarna).
    private const string PositionOutputPrefix = "queue-position-";
    private const int MaxAnnouncedPosition = 20; // verder achteraan: geen melding (cache niet eindeloos laten groeien)

    // App-arg waarmee een door de backend gebelde leg zichzelf identificeert bij StasisStart:
    // "agent" = leg die met de beller verbonden wordt, "consult" = overlegleg (warm doorverbinden).
    private const string AgentAppArg = "agent";
    private const string ConsultAppArg = "consult";

    private readonly IAriClient _ari;
    private readonly AgentStateService _agents;
    private readonly IDbContextFactory<CcDbContext> _dbFactory;
    private readonly IRealtimeNotifier _notifier;
    private readonly ITtsService _tts;
    private readonly int _announceSeconds;
    private readonly ILogger<CallCoordinator> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _holdingBridges = new(StringComparer.Ordinal); // queue → bridgeId
    private readonly ConcurrentDictionary<string, string> _mohByQueue = new(StringComparer.Ordinal); // queue → MoH-klasse
    private readonly List<WaitingCaller> _waiting = [];
    private readonly Dictionary<string, PendingConnect> _pending = new(StringComparer.Ordinal); // agentChannelId → pending
    private readonly Dictionary<string, ActiveCall> _activeByChannel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WarmTransfer> _warmByAgent = new(StringComparer.OrdinalIgnoreCase); // doorverbindende agent → overleg

    private readonly Channel<byte> _dispatchSignals =
        Channel.CreateBounded<byte>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private Task? _announceTask;

    public CallCoordinator(IAriClient ari, AgentStateService agents,
        IDbContextFactory<CcDbContext> dbFactory, IRealtimeNotifier notifier, ITtsService tts,
        IConfiguration config, ILogger<CallCoordinator> logger)
    {
        _ari = ari;
        _agents = agents;
        _dbFactory = dbFactory;
        _notifier = notifier;
        _tts = tts;
        _announceSeconds = config.GetValue("Queue:PositionAnnounceSeconds", 30);
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

    /// <summary>
    /// Een lopend warm doorverbinden (overleg). De beller staat in de holding-brug (wachtmuziek);
    /// de doorverbindende agent (From) blijft in de mixing-brug, die nu als overlegbrug dient. Zodra
    /// de geraadpleegde collega (Consult) opneemt komt die in dezelfde brug → From en Consult overleggen.
    /// Voltooien zet de beller bij Consult; annuleren haalt de beller terug bij From.
    /// </summary>
    private sealed class WarmTransfer
    {
        public required string FromAgentName { get; init; }
        public required string FromAgentChannelId { get; init; }
        public required string ConsultAgentName { get; init; }
        public required string ConsultChannelId { get; init; }
        public required string BridgeId { get; init; } // hergebruikte mixing-brug, nu overlegbrug
        public required string CallerChannelId { get; init; }
        public required string CallerId { get; init; }
        public required string QueueName { get; init; }
        public bool Connected { get; set; } // collega heeft het overleg aangenomen
    }

    // --- Inkomende beller -----------------------------------------------------

    /// <summary>Plaatst een beantwoorde beller in de wacht (holding-brug + wachtmuziek) en triggert dispatch.</summary>
    public async Task EnqueueCallerAsync(string callerChannelId, string queueName, string callerId,
        CancellationToken ct = default)
    {
        _mohByQueue[queueName] = await ResolveMohClassAsync(queueName, ct); // huidige MoH-klasse, vóór de lock
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
        string? resetAgent = null;
        WaitingCaller? requeue = null;
        var signalDispatch = false;
        List<WaitingCallView> snapshot;
        await _gate.WaitAsync(ct);
        try
        {
            // 0) lopend overleg (warm doorverbinden): beller, agent of collega valt weg
            if (FindWarmTransfer(channelId) is { } warm)
            {
                (wrapUpAgent, resetAgent, signalDispatch) = await HandleWarmChannelGoneAsync(warm, channelId, ct);
            }
            // 1) actief gesprek: ruim de brug op, hang de andere leg op, agent → nawerktijd
            else if (_activeByChannel.TryGetValue(channelId, out var call))
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
        if (resetAgent is not null)
            await _agents.ResetToAvailableAsync(resetAgent, ct);
        if (requeue is not null || signalDispatch)
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

    // --- Warm doorverbinden (overleg) -----------------------------------------

    /// <summary>
    /// Start een overleg met een collega: de beller gaat in de wacht (holding-brug + wachtmuziek),
    /// de huidige agent blijft achter in de mixing-brug (die nu als overlegbrug dient) en de collega
    /// wordt gebeld. Bij opnemen komt de collega in dezelfde brug. Faalt als er geen lopend gesprek
    /// is, er al een overleg loopt, of de collega offline/bezet is.
    /// </summary>
    public async Task<bool> StartWarmTransferAsync(string fromAgent, string toAgentName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var call = _activeByChannel.Values.FirstOrDefault(c => c.AgentName == fromAgent);
            if (call is null || _warmByAgent.ContainsKey(fromAgent))
                return false;
            var reserved = await _agents.TryReserveSpecificAsync(toAgentName, ct);
            if (reserved is null)
                return false; // collega offline of al in gesprek

            // beller parkeren; de agent blijft achter in de mixing-brug (wordt overlegbrug)
            _activeByChannel.Remove(call.CallerChannelId);
            _activeByChannel.Remove(call.AgentChannelId);
            await PlaceInHoldingAsync(call.QueueName, call.CallerChannelId, ct);

            try
            {
                var consultChannel =
                    await _ari.OriginateToStasisAsync(reserved.Endpoint, ConsultAppArg, call.CallerId, ct: ct);
                _warmByAgent[fromAgent] = new WarmTransfer
                {
                    FromAgentName = fromAgent,
                    FromAgentChannelId = call.AgentChannelId,
                    ConsultAgentName = reserved.Name,
                    ConsultChannelId = consultChannel,
                    BridgeId = call.MixingBridgeId,
                    CallerChannelId = call.CallerChannelId,
                    CallerId = call.CallerId,
                    QueueName = call.QueueName,
                };
                _logger.LogInformation("Overleg gestart: agent {From} raadpleegt {To} over beller {Caller}",
                    fromAgent, reserved.Name, call.CallerChannelId);
                return true;
            }
            catch (Exception ex)
            {
                // originate mislukt: beller terug bij de agent, collega vrijgeven
                _logger.LogError(ex, "Originate naar collega {Agent} mislukt; overleg afgebroken", reserved.Name);
                await _ari.AddToBridgeAsync(call.MixingBridgeId, call.CallerChannelId, ct);
                call.OnHold = false;
                _activeByChannel[call.CallerChannelId] = call;
                _activeByChannel[call.AgentChannelId] = call;
                await _agents.ReleaseReservationAsync(reserved.Name, ct);
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>De geraadpleegde collega nam het overleg aan: voeg die toe aan de overlegbrug.</summary>
    public async Task OnConsultAnsweredAsync(string consultChannelId, CancellationToken ct = default)
    {
        string? confirmAgent = null;
        await _gate.WaitAsync(ct);
        try
        {
            var wt = _warmByAgent.Values.FirstOrDefault(w => w.ConsultChannelId == consultChannelId);
            if (wt is null)
            {
                _logger.LogWarning("Overlegleg {Channel} nam op maar er is geen lopend overleg", consultChannelId);
                await SafeHangupAsync(consultChannelId, ct);
                return;
            }
            await _ari.AddToBridgeAsync(wt.BridgeId, consultChannelId, ct);
            wt.Connected = true;
            confirmAgent = wt.ConsultAgentName;
            _logger.LogInformation("Collega {Agent} neemt deel aan overleg (brug {Bridge})",
                wt.ConsultAgentName, wt.BridgeId);
        }
        finally
        {
            _gate.Release();
        }

        if (confirmAgent is not null)
            await _agents.ConfirmOnCallAsync(confirmAgent, ct);
    }

    /// <summary>
    /// Voltooi het overleg: de doorverbindende agent stapt eruit (→ nawerktijd) en de beller komt
    /// bij de collega in de brug. Kan pas zodra de collega het overleg heeft aangenomen.
    /// </summary>
    public async Task<bool> CompleteWarmTransferAsync(string fromAgent, CancellationToken ct = default)
    {
        string? wrapUpAgent = null;
        await _gate.WaitAsync(ct);
        try
        {
            if (!_warmByAgent.TryGetValue(fromAgent, out var wt) || !wt.Connected)
                return false;
            _warmByAgent.Remove(fromAgent);

            await SafeHangupAsync(wt.FromAgentChannelId, ct);                 // agent-leg eruit
            await _ari.AddToBridgeAsync(wt.BridgeId, wt.CallerChannelId, ct); // beller uit holding → bij collega
            RegisterActiveCall(wt.CallerChannelId, wt.ConsultChannelId, wt.ConsultAgentName,
                wt.QueueName, wt.CallerId, wt.BridgeId);
            wrapUpAgent = wt.FromAgentName;
            _logger.LogInformation("Overleg voltooid: beller {Caller} nu bij collega {Agent}; {From} naar nawerktijd",
                wt.CallerChannelId, wt.ConsultAgentName, wt.FromAgentName);
        }
        finally
        {
            _gate.Release();
        }

        if (wrapUpAgent is not null)
            await _agents.BeginWrapUpAsync(wrapUpAgent, ct);
        return true;
    }

    /// <summary>
    /// Annuleer het overleg: de collega-leg eindigt en de beller gaat terug naar de oorspronkelijke
    /// agent. Werkt ook wanneer de collega nog niet had opgenomen.
    /// </summary>
    public async Task<bool> CancelWarmTransferAsync(string fromAgent, CancellationToken ct = default)
    {
        string? resetAgent = null;
        await _gate.WaitAsync(ct);
        try
        {
            if (!_warmByAgent.Remove(fromAgent, out var wt))
                return false;

            await SafeHangupAsync(wt.ConsultChannelId, ct);                  // collega-leg eruit
            await _ari.AddToBridgeAsync(wt.BridgeId, wt.CallerChannelId, ct); // beller terug bij agent
            RegisterActiveCall(wt.CallerChannelId, wt.FromAgentChannelId, wt.FromAgentName,
                wt.QueueName, wt.CallerId, wt.BridgeId);
            resetAgent = wt.ConsultAgentName;
            _logger.LogInformation("Overleg geannuleerd: beller {Caller} terug bij {From}; collega {Consult} vrij",
                wt.CallerChannelId, wt.FromAgentName, wt.ConsultAgentName);
        }
        finally
        {
            _gate.Release();
        }

        if (resetAgent is not null)
            await _agents.ResetToAvailableAsync(resetAgent, ct);
        return true;
    }

    private WarmTransfer? FindWarmTransfer(string channelId)
        => _warmByAgent.Values.FirstOrDefault(w =>
            w.CallerChannelId == channelId || w.FromAgentChannelId == channelId || w.ConsultChannelId == channelId);

    /// <summary>
    /// Een kanaal van een lopend overleg viel weg. Aangeroepen terwijl _gate vastgehouden wordt; geeft
    /// terug welke nazorg buiten de lock moet (nawerktijd-agent, vrij te geven collega, herdispatch).
    /// </summary>
    private async Task<(string? WrapUp, string? Reset, bool Requeued)> HandleWarmChannelGoneAsync(
        WarmTransfer wt, string channelId, CancellationToken ct)
    {
        _warmByAgent.Remove(wt.FromAgentName);

        if (channelId == wt.CallerChannelId)
        {
            // beller gaf op tijdens het overleg: beide agent-legs eruit, overlegbrug opruimen
            await SafeHangupAsync(wt.FromAgentChannelId, ct);
            await SafeHangupAsync(wt.ConsultChannelId, ct);
            await SafeDestroyBridgeAsync(wt.BridgeId, ct);
            _logger.LogInformation("Beller hing op tijdens overleg; {From} naar nawerktijd, collega {Consult} vrij",
                wt.FromAgentName, wt.ConsultAgentName);
            return (wt.FromAgentName, wt.ConsultAgentName, false);
        }

        if (channelId == wt.ConsultChannelId)
        {
            // collega nam niet op of hing op: beller terug bij de oorspronkelijke agent
            await _ari.AddToBridgeAsync(wt.BridgeId, wt.CallerChannelId, ct);
            RegisterActiveCall(wt.CallerChannelId, wt.FromAgentChannelId, wt.FromAgentName,
                wt.QueueName, wt.CallerId, wt.BridgeId);
            _logger.LogInformation("Collega {Consult} verliet het overleg; beller terug bij {From}",
                wt.ConsultAgentName, wt.FromAgentName);
            return (null, wt.ConsultAgentName, false);
        }

        // channelId == wt.FromAgentChannelId: de doorverbindende agent verbrak zelf
        if (wt.Connected)
        {
            // collega zat al in het overleg: draag de beller over (alsof voltooid)
            await _ari.AddToBridgeAsync(wt.BridgeId, wt.CallerChannelId, ct);
            RegisterActiveCall(wt.CallerChannelId, wt.ConsultChannelId, wt.ConsultAgentName,
                wt.QueueName, wt.CallerId, wt.BridgeId);
            _logger.LogInformation("Agent {From} verbrak tijdens overleg; beller overgedragen aan {Consult}",
                wt.FromAgentName, wt.ConsultAgentName);
            return (wt.FromAgentName, null, false);
        }

        // collega had nog niet opgenomen: beller terug in de wacht, collega vrijgeven
        await SafeHangupAsync(wt.ConsultChannelId, ct);
        _waiting.Add(new WaitingCaller(wt.CallerChannelId, wt.QueueName, wt.CallerId, DateTimeOffset.UtcNow));
        _logger.LogInformation("Agent {From} verbrak vóór het overleg; beller terug in de wacht voor '{Queue}'",
            wt.FromAgentName, wt.QueueName);
        return (wt.FromAgentName, wt.ConsultAgentName, true);
    }

    private void RegisterActiveCall(string callerChannelId, string agentChannelId, string agentName,
        string queueName, string callerId, string bridgeId)
    {
        var call = new ActiveCall
        {
            CallerChannelId = callerChannelId,
            AgentChannelId = agentChannelId,
            AgentName = agentName,
            QueueName = queueName,
            CallerId = callerId,
            MixingBridgeId = bridgeId,
        };
        _activeByChannel[callerChannelId] = call;
        _activeByChannel[agentChannelId] = call;
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

    // --- Positie-meldingen ----------------------------------------------------

    private async Task AnnouncePumpAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_announceSeconds));
            while (await timer.WaitForNextTickAsync(ct))
                await AnnouncePositionsAsync(ct);
        }
        catch (OperationCanceledException) { /* afsluiten */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Positie-aankondiger gestopt door onverwachte fout");
        }
    }

    /// <summary>
    /// Vertelt elke wachtende beller zijn huidige positie in de eigen wachtrij (oudste = 1). De
    /// melding wordt per positienummer eenmalig via Piper gegenereerd en gecachet; bij uitgeschakelde
    /// of falende TTS gebeurt er niets (de wachtmuziek blijft ongemoeid).
    /// </summary>
    internal async Task AnnouncePositionsAsync(CancellationToken ct = default)
    {
        if (!_tts.IsEnabled)
            return;

        // Snapshot onder de lock: bepaal de positie per beller; afspelen gebeurt erbuiten zodat
        // de dispatch-pomp niet blokkeert.
        List<(string ChannelId, int Position)> targets;
        await _gate.WaitAsync(ct);
        try
        {
            var perQueue = new Dictionary<string, int>(StringComparer.Ordinal);
            targets = [];
            foreach (var caller in _waiting) // invoegvolgorde = wachtvolgorde
            {
                var pos = perQueue.GetValueOrDefault(caller.QueueName) + 1;
                perQueue[caller.QueueName] = pos;
                targets.Add((caller.ChannelId, pos));
            }
        }
        finally
        {
            _gate.Release();
        }

        foreach (var (channelId, position) in targets)
            await AnnouncePositionAsync(channelId, position, ct);
    }

    private async Task AnnouncePositionAsync(string channelId, int position, CancellationToken ct)
    {
        if (position > MaxAnnouncedPosition)
            return;

        var outputName = $"{PositionOutputPrefix}{position}";
        if (!_tts.OutputExists(outputName))
        {
            var text = $"U bent nummer {position} in de wachtrij. Een moment geduld alstublieft.";
            if (!await _tts.SynthesizeAsync(text, _tts.DefaultVoice, outputName, ct))
                return; // synthese mislukt: laat de wachtmuziek ongemoeid
        }

        try
        {
            await _ari.PlayAsync(channelId, $"sound:custom/{outputName}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Positie-melding voor {Channel} afspelen faalde: {Reden}", channelId, ex.Message);
        }
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
        await _ari.StartBridgeMohAsync(bridgeId, _mohByQueue.GetValueOrDefault(queueName, DefaultMohClass), ct);
    }

    /// <summary>Leest de music-on-hold-klasse van de wachtrij; terugval op "default" bij fout/leeg.</summary>
    private async Task<string> ResolveMohClassAsync(string queueName, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var moh = await db.Queues.AsNoTracking()
                .Where(q => q.Name == queueName)
                .Select(q => q.MusicOnHoldClass)
                .FirstOrDefaultAsync(ct);
            return string.IsNullOrWhiteSpace(moh) ? DefaultMohClass : moh;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MoH-klasse voor '{Queue}' niet leesbaar ({Reden}); standaard gebruikt",
                queueName, ex.Message);
            return DefaultMohClass;
        }
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
        if (_announceSeconds > 0)
            _announceTask = AnnouncePumpAsync(_pumpCts.Token);
        else
            _logger.LogInformation("Positie-meldingen uitgeschakeld (Queue:PositionAnnounceSeconds = 0)");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _dispatchSignals.Writer.TryComplete();
        if (_pumpCts is not null)
            await _pumpCts.CancelAsync();
        var pending = new[] { _pumpTask, _announceTask }.Where(t => t is not null).Cast<Task>().ToArray();
        if (pending.Length > 0)
            await Task.WhenAny(Task.WhenAll(pending), Task.Delay(Timeout.Infinite, cancellationToken));
    }
}
