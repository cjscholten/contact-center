namespace ContactCenter.Api.Auth;

/// <summary>
/// Zet de tenant van de huidige request op basis van de token-issuer (realm). Draait ná
/// authenticatie: voor een geauthenticeerde gebruiker wordt issuer → realm → tenant herleid en
/// in de <see cref="ITenantAccessor"/> gezet, zodat de EF query-filter de data scope. Een
/// geauthenticeerd token van een onbekende/uitgeschakelde realm wordt geweigerd (401).
/// Anonieme requests (bv. /health) gaan ongemoeid door.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next, ITenantRegistry registry, ILogger<TenantMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITenantAccessor accessor)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var issuer = context.User.Claims
                .Select(c => c.Issuer)
                .FirstOrDefault(i => KeycloakRealmKeys.RealmFromIssuer(i) is not null);
            var realm = KeycloakRealmKeys.RealmFromIssuer(issuer);

            if (realm is null || !registry.TryGetByRealm(realm, out var tenant))
            {
                logger.LogWarning("Token met onbekende/uitgeschakelde realm geweigerd (issuer '{Issuer}')", issuer);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            accessor.TenantId = tenant.Id;
        }

        await next(context);
    }
}
