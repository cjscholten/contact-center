namespace ContactCenter.Api.Data;

public class QueueConfig
{
    public int Id { get; set; }

    /// <summary>Technische naam; moet overeenkomen met de wachtrij in queues.conf ([a-z0-9]).</summary>
    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Asterisk media-URI, bv. "sound:queue-thankyou". Eigen (NL) prompts volgen later.</summary>
    public string WelcomePrompt { get; set; } = "sound:queue-thankyou";

    public string ClosedPrompt { get; set; } = "sound:vm-goodbye";

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
