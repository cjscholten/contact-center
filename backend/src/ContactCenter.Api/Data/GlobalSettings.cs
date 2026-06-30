namespace ContactCenter.Api.Data;

/// <summary>Eén rij met instellingen per tenant.</summary>
public class GlobalSettings
{
    public int Id { get; set; }

    /// <summary>Eigenaar (klant) van deze instellingen. Zie <see cref="Tenant"/>.</summary>
    public int TenantId { get; set; }

    /// <summary>Nawerktijd in seconden na elk gesprek; 0 schakelt nawerktijd uit.</summary>
    public int WrapUpSeconds { get; set; } = 30;
}
