using ContactCenter.Api.Agents;
using ContactCenter.Api.CallFlow;

namespace ContactCenter.Api.Realtime;

/// <summary>Pusht wijzigingen naar de agent-schermen van één tenant. Geïmplementeerd via SignalR; in tests gefaket.</summary>
public interface IRealtimeNotifier
{
    /// <summary>Pusht de actuele wachtrij-stand van <paramref name="tenantId"/> naar de schermen van die tenant.</summary>
    Task QueuesChangedAsync(int tenantId, IReadOnlyList<WaitingCallView> waiting, CancellationToken ct = default);

    /// <summary>Pusht de nieuwe snapshot van één agent naar diens eigen scherm (per-agent-groep).</summary>
    Task AgentChangedAsync(int tenantId, AgentSnapshot snapshot, CancellationToken ct = default);
}

/// <summary>Naamgeving van de SignalR-groepen per tenant en per agent; gedeeld tussen hub en notifier.</summary>
public static class TenantGroups
{
    public static string For(int tenantId) => $"tenant-{tenantId}";

    /// <summary>Groep met alleen de schermen van één agent (voor persoonlijke status-updates).</summary>
    public static string ForAgent(int tenantId, string agentName) => $"tenant-{tenantId}-agent-{agentName.ToLowerInvariant()}";
}
