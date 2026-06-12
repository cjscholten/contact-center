using ContactCenter.Api.Ami;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.Agents;

/// <summary>
/// Statusmachine per agent: LoggedOut → Available → OnCall → WrapUp → Available.
/// Status leeft in-memory (herstart backend = iedereen uitgelogd); wachtrij-
/// lidmaatschap wordt via AMI in Asterisk gespiegeld.
/// </summary>
public sealed class AgentStateService(
    IQueueMemberControl queues,
    IDbContextFactory<CcDbContext> dbFactory,
    ILogger<AgentStateService> logger)
{
    private const int DefaultWrapUpSeconds = 30;
    private const string WrapUpPauseReason = "nawerktijd";

    private sealed class RuntimeState
    {
        public required string Name { get; init; }
        public required string DisplayName { get; init; }
        public required string Endpoint { get; init; }
        public required List<string> QueueNames { get; init; }
        public AgentStatus Status { get; set; } = AgentStatus.Available;
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

        await _gate.WaitAsync(ct);
        try
        {
            var state = new RuntimeState
            {
                Name = agent.Name,
                DisplayName = agent.DisplayName,
                Endpoint = agent.Endpoint,
                QueueNames = [.. agent.QueueAssignments.Select(qa => qa.Queue!.Name)],
            };

            foreach (var queue in state.QueueNames)
                await queues.AddAsync(queue, state.Endpoint, state.DisplayName, ct);

            // her-login reset een eventuele nawerktijd-pauze
            if (_loggedIn.TryGetValue(name, out var previous))
                await CancelWrapUpAsync(previous, ct);

            _loggedIn[name] = state;
            logger.LogInformation("Agent {Agent} ingelogd in wachtrijen: {Queues}",
                name, string.Join(", ", state.QueueNames));
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentSnapshot?> LogoutAsync(string name, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_loggedIn.Remove(name, out var state))
                return null;

            state.WrapUpTimer?.Cancel();
            foreach (var queue in state.QueueNames)
                await queues.RemoveAsync(queue, state.Endpoint, ct);

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

    /// <summary>Beëindigt de nawerktijd vroegtijdig (de "klaar"-knop).</summary>
    public async Task<AgentSnapshot?> FinishWrapUpAsync(string name, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_loggedIn.TryGetValue(name, out var state))
                return null;
            if (state.Status == AgentStatus.WrapUp)
                await CancelWrapUpAsync(state, ct);
            return Snapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HandleChannelUpAsync(string channelName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = FindByChannel(channelName);
            if (state is null || state.Status == AgentStatus.OnCall)
                return;

            state.WrapUpTimer?.Cancel();
            state.WrapUpTimer = null;
            SetStatus(state, AgentStatus.OnCall);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HandleChannelDestroyedAsync(string channelName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = FindByChannel(channelName);
            if (state is null || state.Status != AgentStatus.OnCall)
                return;

            var seconds = await GetWrapUpSecondsAsync(ct);
            if (seconds <= 0)
            {
                SetStatus(state, AgentStatus.Available);
                return;
            }

            await queues.SetPausedAsync(state.Endpoint, paused: true, WrapUpPauseReason, ct);
            SetStatus(state, AgentStatus.WrapUp);
            StartWrapUpTimer(state, TimeSpan.FromSeconds(seconds));
        }
        finally
        {
            _gate.Release();
        }
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
        return agent is null ? null : new AgentSnapshot(agent.Name, agent.DisplayName, AgentStatus.LoggedOut, default);
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
                : new AgentSnapshot(a.Name, a.DisplayName, AgentStatus.LoggedOut, default))];
        }
        finally
        {
            _gate.Release();
        }
    }

    private RuntimeState? FindByChannel(string channelName)
        => _loggedIn.Values.FirstOrDefault(s => ChannelNames.BelongsToEndpoint(channelName, s.Endpoint));

    private async Task CancelWrapUpAsync(RuntimeState state, CancellationToken ct)
    {
        state.WrapUpTimer?.Cancel();
        state.WrapUpTimer = null;
        if (state.Status == AgentStatus.WrapUp)
        {
            await queues.SetPausedAsync(state.Endpoint, paused: false, WrapUpPauseReason, ct);
            SetStatus(state, AgentStatus.Available);
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
                // vroegtijdig beëindigd via klaar-knop, nieuw gesprek of logout
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
        => new(state.Name, state.DisplayName, state.Status, state.Since);
}
