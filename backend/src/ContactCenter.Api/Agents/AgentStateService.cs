using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.Agents;

/// <summary>
/// Statusmachine per agent. Twee assen: de systeem-bepaalde gespreksfase (AgentStatus:
/// Available → Ringing → OnCall → WrapUp) en de handmatige beschikbaarheid (Presence:
/// Available/Break/Unavailable). Kiesbaar voor automatische toewijzing = gespreksfase
/// Available én Presence Available. Status leeft in-memory (herstart = iedereen uitgelogd).
/// </summary>
public sealed class AgentStateService(
    IDbContextFactory<CcDbContext> dbFactory,
    ILogger<AgentStateService> logger)
{
    private const int DefaultWrapUpSeconds = 30;

    /// <summary>Niet-blokkerend signaal dat er werk te verdelen valt (geen await → geen lock-cykel).</summary>
    public Action? RequestDispatch { get; set; }

    private sealed class RuntimeState
    {
        public required string Name { get; init; }
        public required string DisplayName { get; init; }
        public required string Endpoint { get; init; }
        public required List<string> QueueNames { get; init; }
        public AgentStatus Status { get; set; } = AgentStatus.Available;
        public Presence Presence { get; set; } = Presence.Available;
        public DateTimeOffset Since { get; set; } = DateTimeOffset.UtcNow;
        public CancellationTokenSource? WrapUpTimer { get; set; }
    }

    private readonly Dictionary<string, RuntimeState> _loggedIn = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AgentSnapshot?> LoginAsync(string name, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var agent = await db.Agents.AsNoTracking()
            .Include(a => a.QueueAssignments).ThenInclude(qa => qa.Queue)
            .FirstOrDefaultAsync(a => a.Name == name, ct);
        if (agent is null)
            return null;

        AgentSnapshot snapshot;
        await _gate.WaitAsync(ct);
        try
        {
            if (_loggedIn.TryGetValue(name, out var existing))
                existing.WrapUpTimer?.Cancel();

            var state = new RuntimeState
            {
                Name = agent.Name,
                DisplayName = agent.DisplayName,
                Endpoint = agent.Endpoint,
                QueueNames = [.. agent.QueueAssignments.Select(qa => qa.Queue!.Name)],
            };
            _loggedIn[name] = state;
            logger.LogInformation("Agent {Agent} ingelogd, wachtrijen: {Queues}",
                name, string.Join(", ", state.QueueNames));
            snapshot = Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }

        RequestDispatch?.Invoke();
        return snapshot;
    }

    public async Task<AgentSnapshot?> LogoutAsync(string name, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_loggedIn.Remove(name, out var state))
                return null;
            state.WrapUpTimer?.Cancel();
            state.Status = AgentStatus.LoggedOut;
            state.Since = DateTimeOffset.UtcNow;
            logger.LogInformation("Agent {Agent} uitgelogd", name);
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Handmatige beschikbaarheid zetten. Naar Available maakt de agent (weer) kiesbaar.</summary>
    public async Task<AgentSnapshot?> SetPresenceAsync(string name, Presence presence, CancellationToken ct = default)
    {
        AgentSnapshot? snapshot;
        await _gate.WaitAsync(ct);
        try
        {
            if (!_loggedIn.TryGetValue(name, out var state))
                return null;
            state.Presence = presence;
            state.Since = DateTimeOffset.UtcNow;
            logger.LogInformation("Agent {Agent} presence → {Presence}", name, presence);
            snapshot = Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }

        if (presence == Presence.Available)
            RequestDispatch?.Invoke();
        return snapshot;
    }

    /// <summary>Automatische toewijzing: reserveert een kiesbare agent in één van de wachtrijen.</summary>
    public async Task<ReservedAgent?> TryReserveForCallAsync(IReadOnlyCollection<string> queueNames,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = _loggedIn.Values.FirstOrDefault(s =>
                s.Status == AgentStatus.Available
                && s.Presence == Presence.Available
                && s.QueueNames.Any(queueNames.Contains));
            if (state is null)
                return null;
            SetStatus(state, AgentStatus.Ringing);
            return new ReservedAgent(state.Name, state.Endpoint);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Handmatig aannemen: reserveert déze agent als die geen lopend gesprek heeft (presence-onafhankelijk).</summary>
    public async Task<ReservedAgent?> TryReserveSpecificAsync(string name, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_loggedIn.TryGetValue(name, out var state) && state.Status == AgentStatus.Available)
            {
                SetStatus(state, AgentStatus.Ringing);
                return new ReservedAgent(state.Name, state.Endpoint);
            }
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ConfirmOnCallAsync(string name, CancellationToken ct = default)
        => TransitionAsync(name, AgentStatus.OnCall, ct);

    /// <summary>Originate mislukte of werd niet beantwoord: agent weer beschikbaar.</summary>
    public async Task ReleaseReservationAsync(string name, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_loggedIn.TryGetValue(name, out var state) && state.Status == AgentStatus.Ringing)
                SetStatus(state, AgentStatus.Available);
        }
        finally
        {
            _gate.Release();
        }
        RequestDispatch?.Invoke();
    }

    /// <summary>Gesprek beëindigd: start nawerktijd (of meteen beschikbaar als die 0 is).</summary>
    public async Task BeginWrapUpAsync(string name, CancellationToken ct = default)
    {
        var seconds = await GetWrapUpSecondsAsync(ct);

        await _gate.WaitAsync(ct);
        try
        {
            if (!_loggedIn.TryGetValue(name, out var state) || state.Status == AgentStatus.LoggedOut)
                return;
            if (seconds <= 0)
            {
                SetStatus(state, AgentStatus.Available);
            }
            else
            {
                SetStatus(state, AgentStatus.WrapUp);
                StartWrapUpTimer(state, TimeSpan.FromSeconds(seconds));
                return;
            }
        }
        finally
        {
            _gate.Release();
        }
        RequestDispatch?.Invoke();
    }

    /// <summary>Beëindigt de nawerktijd vroegtijdig (de "klaar"-knop).</summary>
    public async Task<AgentSnapshot?> FinishWrapUpAsync(string name, CancellationToken ct = default)
    {
        AgentSnapshot? snapshot;
        var becameAvailable = false;
        await _gate.WaitAsync(ct);
        try
        {
            if (!_loggedIn.TryGetValue(name, out var state))
                return null;
            if (state.Status == AgentStatus.WrapUp)
            {
                state.WrapUpTimer?.Cancel();
                state.WrapUpTimer = null;
                SetStatus(state, AgentStatus.Available);
                becameAvailable = true;
            }
            snapshot = Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }

        if (becameAvailable)
            RequestDispatch?.Invoke();
        return snapshot;
    }

    public async Task<AgentSnapshot?> GetAsync(string name, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_loggedIn.TryGetValue(name, out var state))
                return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Name == name, ct);
        return agent is null ? null : LoggedOutSnapshot(agent.Name, agent.DisplayName);
    }

    public async Task<IReadOnlyList<AgentSnapshot>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var agents = await db.Agents.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);

        await _gate.WaitAsync(ct);
        try
        {
            return [.. agents.Select(a => _loggedIn.TryGetValue(a.Name, out var state)
                ? Snapshot(state)
                : LoggedOutSnapshot(a.Name, a.DisplayName))];
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TransitionAsync(string name, AgentStatus status, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_loggedIn.TryGetValue(name, out var state))
                SetStatus(state, status);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StartWrapUpTimer(RuntimeState state, TimeSpan duration)
    {
        var cts = new CancellationTokenSource();
        state.WrapUpTimer = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(duration, cts.Token);
                await FinishWrapUpAsync(state.Name);
            }
            catch (OperationCanceledException)
            {
                // vroegtijdig beëindigd via knop, nieuw gesprek of logout
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nawerktijd afronden voor {Agent} mislukt", state.Name);
            }
        });
    }

    private async Task<int> GetWrapUpSecondsAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
            return settings?.WrapUpSeconds ?? DefaultWrapUpSeconds;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Nawerktijd-instelling niet leesbaar ({Reden}); standaard {Seconds}s",
                ex.Message, DefaultWrapUpSeconds);
            return DefaultWrapUpSeconds;
        }
    }

    private void SetStatus(RuntimeState state, AgentStatus status)
    {
        state.Status = status;
        state.Since = DateTimeOffset.UtcNow;
        logger.LogInformation("Agent {Agent} → {Status}", state.Name, status);
    }

    private static AgentSnapshot Snapshot(RuntimeState state)
        => new(state.Name, state.DisplayName, state.Status, state.Presence, state.Since);

    private static AgentSnapshot LoggedOutSnapshot(string name, string displayName)
        => new(name, displayName, AgentStatus.LoggedOut, Presence.Available, default);
}

public sealed record ReservedAgent(string Name, string Endpoint);
