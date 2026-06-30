namespace ContactCenter.Api.Data;

/// <summary>
/// Een klant (tenant). Elke tenant heeft een eigen Keycloak-realm; tokens met die realm als
/// issuer worden naar deze tenant herleid. Alle tenant-eigen data (wachtrijen, agents,
/// contacten, instellingen) draagt de bijbehorende <see cref="Id"/> als <c>TenantId</c>.
/// </summary>
public class Tenant
{
    public int Id { get; set; }

    /// <summary>Korte, URL-veilige sleutel (bv. "default", "acme"); ook gebruikt voor endpoint-namespacing.</summary>
    public required string Slug { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Naam van de Keycloak-realm van deze tenant (bv. "contactcenter" of "tenant-acme").</summary>
    public required string Realm { get; set; }

    public bool Enabled { get; set; } = true;
}
