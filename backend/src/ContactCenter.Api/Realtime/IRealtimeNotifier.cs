using ContactCenter.Api.CallFlow;

namespace ContactCenter.Api.Realtime;

/// <summary>Pusht wijzigingen naar de agent-schermen. Geïmplementeerd via SignalR; in tests gefaket.</summary>
public interface IRealtimeNotifier
{
    Task QueuesChangedAsync(IReadOnlyList<WaitingCallView> waiting, CancellationToken ct = default);
}
