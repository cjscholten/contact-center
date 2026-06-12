namespace ContactCenter.Api.Data;

/// <summary>Eén rij met globale instellingen.</summary>
public class GlobalSettings
{
    public int Id { get; set; }

    /// <summary>Nawerktijd in seconden na elk gesprek; 0 schakelt nawerktijd uit.</summary>
    public int WrapUpSeconds { get; set; } = 30;
}
