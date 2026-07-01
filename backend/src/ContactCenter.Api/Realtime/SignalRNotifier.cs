using ContactCenter.Api.Agents;
using ContactCenter.Api.CallFlow;
using Microsoft.AspNetCore.SignalR;

namespace ContactCenter.Api.Realtime;

public sealed class SignalRNotifier(IHubContext<ContactCenterHub> hub) : IRealtimeNotifier
{
    public Task QueuesChangedAsync(int tenantId, IReadOnlyList<WaitingCallView> waiting, CancellationToken ct = default)
        => hub.Clients.Group(TenantGroups.For(tenantId)).SendAsync("queuesChanged", waiting, ct);

    public Task AgentChangedAsync(int tenantId, AgentSnapshot snapshot, CancellationToken ct = default)
        => hub.Clients.Group(TenantGroups.ForAgent(tenantId, snapshot.Name)).SendAsync("agentChanged", snapshot, ct);
}
