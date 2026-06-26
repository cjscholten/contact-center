using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace ContactCenter.Api.Auth;

/// <summary>
/// Keycloak zet realm-rollen in de claim <c>realm_access</c> (JSON met een <c>roles</c>-array),
/// niet als losse role-claims. Deze transformatie haalt ze eruit en voegt ze als
/// <see cref="ClaimTypes.Role"/>-claims toe, zodat <c>RequireRole("admin")</c> e.d. werken.
/// </summary>
public sealed class KeycloakRolesTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity)
            return Task.FromResult(principal);

        var realmAccess = principal.FindFirst("realm_access")?.Value;
        if (!string.IsNullOrEmpty(realmAccess))
        {
            try
            {
                using var doc = JsonDocument.Parse(realmAccess);
                if (doc.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
                {
                    foreach (var role in roles.EnumerateArray())
                    {
                        var name = role.GetString();
                        if (!string.IsNullOrEmpty(name) && !identity.HasClaim(ClaimTypes.Role, name))
                            identity.AddClaim(new Claim(ClaimTypes.Role, name));
                    }
                }
            }
            catch (JsonException)
            {
                // onverwacht formaat — laat de rollen weg in plaats van te crashen
            }
        }

        return Task.FromResult(principal);
    }
}
