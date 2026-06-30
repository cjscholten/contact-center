using System.Collections.Concurrent;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ContactCenter.Api.Auth;

/// <summary>Keycloak-endpoints. Bij multi-tenant verschilt alleen de realm; de basis-URL is gedeeld.</summary>
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Basis-URL waar de backend de OIDC-metadata/JWKS ophaalt (in de container: localhost).</summary>
    public string BaseUrl { get; set; } = "http://localhost:8080";
}

/// <summary>
/// Haalt per Keycloak-realm de OIDC-metadata + JWKS op en cachet die. Eén
/// <see cref="ConfigurationManager{T}"/> per realm zodat sleutelrotatie netjes wordt opgevolgd.
/// Gebruikt door de dynamische JWT-validatie: de realm wordt uit de token-issuer afgeleid.
/// </summary>
public sealed class KeycloakRealmKeys(KeycloakOptions options)
{
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new();

    /// <summary>Realmnaam uit een issuer-URL <c>.../realms/&lt;realm&gt;</c>; <c>null</c> als die vorm niet klopt.</summary>
    public static string? RealmFromIssuer(string? issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer)) return null;
        const string marker = "/realms/";
        var idx = issuer.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var realm = issuer[(idx + marker.Length)..].Trim('/');
        return realm.Length == 0 ? null : realm;
    }

    /// <summary>De actuele ondertekensleutels van de realm (gecachet, met automatische refresh).</summary>
    public IEnumerable<SecurityKey> SigningKeys(string realm)
    {
        var manager = _managers.GetOrAdd(realm, r =>
        {
            var metadataAddress = $"{options.BaseUrl.TrimEnd('/')}/realms/{r}/.well-known/openid-configuration";
            return new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = false });
        });
        var config = manager.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
        return config.SigningKeys;
    }
}
