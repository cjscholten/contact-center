using ContactCenter.Api.CallFlow;

namespace ContactCenter.Api.Realtime;

/// <summary>Pusht wijzigingen naar de agent-schermen van één tenant. Geïmplementeerd via SignalR; in tests gefaket.</summary>
public interface IRealtimeNotifier
{
    /// <summary>Pusht de actuele wachtrij-stand van <paramref name="tenantId"/> naar de schermen van die tenant.</summary>
    Task QueuesChangedAsync(int tenantId, IReadOnlyList<WaitingCallView> waiting, CancellationToken ct = default);
}

/// <summary>Naamgeving van de SignalR-groep per tenant; gedeeld tussen hub en notifier.</summary>
public static class TenantGroups
{
    public static string For(int tenantId) => $"tenant-{tenantId}";
}
