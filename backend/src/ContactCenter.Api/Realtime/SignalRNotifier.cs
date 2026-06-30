using ContactCenter.Api.CallFlow;
using Microsoft.AspNetCore.SignalR;

namespace ContactCenter.Api.Realtime;

public sealed class SignalRNotifier(IHubContext<ContactCenterHub> hub) : IRealtimeNotifier
{
    public Task QueuesChangedAsync(int tenantId, IReadOnlyList<WaitingCallView> waiting, CancellationToken ct = default)
        => hub.Clients.Group(TenantGroups.For(tenantId)).SendAsync("queuesChanged", waiting, ct);
}
