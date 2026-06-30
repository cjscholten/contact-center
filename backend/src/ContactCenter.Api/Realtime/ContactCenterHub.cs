using ContactCenter.Api.Auth;
using Microsoft.AspNetCore.SignalR;

namespace ContactCenter.Api.Realtime;

/// <summary>
/// SignalR-hub voor de agent-schermen. Clients luisteren op "queuesChanged" (en later op
/// agent-status). De initiële stand halen ze via GET /api/queues; updates komen via deze hub.
///
/// Multi-tenant: bij verbinden wordt de connectie in de groep van haar tenant geplaatst (afgeleid
/// uit de token-issuer/realm), zodat wachtrij-updates alleen naar de schermen van die tenant gaan.
/// </summary>
public sealed class ContactCenterHub(ITenantRegistry registry) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var issuer = Context.User?.Claims
            .Select(c => c.Issuer)
            .FirstOrDefault(i => KeycloakRealmKeys.RealmFromIssuer(i) is not null);
        var realm = KeycloakRealmKeys.RealmFromIssuer(issuer);
        if (realm is not null && registry.TryGetByRealm(realm, out var tenant))
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroups.For(tenant.Id));

        await base.OnConnectedAsync();
    }
}
