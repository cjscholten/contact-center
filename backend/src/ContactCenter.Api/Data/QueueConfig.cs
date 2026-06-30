namespace ContactCenter.Api.Data;

public class QueueConfig
{
    public int Id { get; set; }

    /// <summary>Eigenaar (klant) van deze wachtrij. Zie <see cref="Tenant"/>.</summary>
    public int TenantId { get; set; }

    /// <summary>Technische naam ([a-z0-9]); uniek binnen de tenant.</summary>
    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>
    /// Afgespeelde media-URI bij welkom. Wordt automatisch beheerd: bij een ingevulde
    /// <see cref="WelcomeText"/> zet de backend deze op het via TTS gegenereerde "sound:custom/...".
    /// </summary>
    public string WelcomePrompt { get; set; } = "sound:queue-thankyou";

    public string ClosedPrompt { get; set; } = "sound:vm-goodbye";

    /// <summary>Welkomsttekst die via TTS (Piper) naar spraak wordt omgezet; leeg = standaardprompt.</summary>
    public string WelcomeText { get; set; } = "";

    /// <summary>Gesloten-melding (TTS); leeg = standaardprompt.</summary>
    public string ClosedText { get; set; } = "";

    /// <summary>Piper-stem voor de TTS-prompts (moet als model in de backend-container bestaan).</summary>
    public string Voice { get; set; } = "nl_NL-pim-medium";

    /// <summary>Handmatige sluiting, gaat vóór de openingstijden.</summary>
    public bool AdHocClosed { get; set; }

    /// <summary>Indien gezet wordt bij ad-hoc sluiting doorgeschakeld in plaats van de gesloten-tekst.</summary>
    public string? AdHocForwardNumber { get; set; }

    /// <summary>IANA-tijdzone waarin de openingstijden gelden.</summary>
    public string TimeZone { get; set; } = "Europe/Amsterdam";

    /// <summary>Asterisk music-on-hold-klasse voor de wachtmuziek (uit musiconhold.conf).</summary>
    public string MusicOnHoldClass { get; set; } = "default";

    public List<OpeningHoursWindow> OpeningHours { get; set; } = [];

    public List<InboundNumber> Numbers { get; set; } = [];
}
