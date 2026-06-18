using ContactCenter.Api.CallFlow;
using Microsoft.AspNetCore.SignalR;

namespace ContactCenter.Api.Realtime;

public sealed class SignalRNotifier(IHubContext<ContactCenterHub> hub) : IRealtimeNotifier
{
    public Task QueuesChangedAsync(IReadOnlyList<WaitingCallView> waiting, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("queuesChanged", waiting, ct);
}
