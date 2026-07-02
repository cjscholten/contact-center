namespace ContactCenter.Api.Data;

/// <summary>Hoe gesprekken uit deze wachtrij aan agenten worden aangeboden.</summary>
public enum QueueOfferMode
{
    /// <summary>Het systeem verdeelt automatisch (met <see cref="QueueRoutingStrategy"/>).</summary>
    AutoDispatch,

    /// <summary>Geen automatische verdeling; agenten pakken gesprekken zelf uit de wachtrij.</summary>
    ManualPickup,
}

/// <summary>Verdeelmethode bij automatische toewijzing (<see cref="QueueOfferMode.AutoDispatch"/>).</summary>
public enum QueueRoutingStrategy
{
    // LongestIdle staat bewust eerst (CLR-default 0): dat is óók de database-default, zodat EF een
    // expliciet gekozen andere waarde nooit stil door de default vervangt (sentinel-valkuil).

    /// <summary>De agent die het langst geen gesprek kreeg, krijgt de volgende. Standaard.</summary>
    LongestIdle,

    /// <summary>Rinkelt bij alle beschikbare agenten tegelijk; de eerste die opneemt wint.</summary>
    RingAll,

    /// <summary>Strikt lineair: altijd de bovenste beschikbare agent (vaste volgorde op naam).</summary>
    Linear,
}

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

    /// <summary>Hoe gesprekken worden aangeboden: automatisch verdelen of handmatig oppakken.</summary>
    public QueueOfferMode OfferMode { get; set; } = QueueOfferMode.AutoDispatch;

    /// <summary>Verdeelmethode bij automatisch aanbieden (genegeerd bij handmatig oppakken).</summary>
    public QueueRoutingStrategy RoutingStrategy { get; set; } = QueueRoutingStrategy.LongestIdle;

    public List<OpeningHoursWindow> OpeningHours { get; set; } = [];

    public List<InboundNumber> Numbers { get; set; } = [];
}
