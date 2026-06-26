using System.Security.Claims;
using ContactCenter.Api.Auth;

namespace ContactCenter.Tests;

public class KeycloakRolesTransformationTests
{
    private static ClaimsPrincipal PrincipalWith(string? realmAccess)
    {
        var claims = realmAccess is null ? [] : new[] { new Claim("realm_access", realmAccess) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task Realm_rollen_worden_role_claims()
    {
        var result = await new KeycloakRolesTransformation()
            .TransformAsync(PrincipalWith("""{"roles":["agent","admin"]}"""));

        Assert.True(result.IsInRole("agent"));
        Assert.True(result.IsInRole("admin"));
    }

    [Fact]
    public async Task Geen_realm_access_geeft_geen_rollen()
    {
        var result = await new KeycloakRolesTransformation().TransformAsync(PrincipalWith(null));
        Assert.False(result.IsInRole("agent"));
    }

    [Fact]
    public async Task Onverwacht_formaat_crasht_niet()
    {
        var result = await new KeycloakRolesTransformation().TransformAsync(PrincipalWith("geen-json"));
        Assert.False(result.IsInRole("agent"));
    }

    [Fact]
    public async Task Bestaande_rol_wordt_niet_gedupliceerd()
    {
        var identity = new ClaimsIdentity(
            [new Claim("realm_access", """{"roles":["agent"]}"""), new Claim(ClaimTypes.Role, "agent")], "test");

        var result = await new KeycloakRolesTransformation().TransformAsync(new ClaimsPrincipal(identity));

        Assert.Single(result.FindAll(ClaimTypes.Role), c => c.Value == "agent");
    }
}
