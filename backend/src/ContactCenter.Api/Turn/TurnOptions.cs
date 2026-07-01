namespace ContactCenter.Api.Turn;

/// <summary>
/// Configuratie voor de TURN/STUN-server (coturn) waarmee WebRTC-agents achter NAT/thuis media
/// kunnen relayen. <see cref="Secret"/> moet gelijk zijn aan coturn's static-auth-secret; leeg =
/// TURN uitgeschakeld (de softphone valt dan terug op host-/STUN-kandidaten, zoals voorheen).
/// </summary>
public sealed class TurnOptions
{
    public const string SectionName = "Turn";

    /// <summary>Gedeeld geheim voor tijdelijke credentials (coturn use-auth-secret). Uit env/infra/.env.</summary>
    public string Secret { get; set; } = "";

    /// <summary>Publieke host/IP van coturn die de browser bereikt (bv. 20.107.0.204).</summary>
    public string Host { get; set; } = "";

    /// <summary>Luisterpoort van coturn (STUN/TURN).</summary>
    public int Port { get; set; } = 3478;

    /// <summary>Geldigheidsduur van een uitgegeven credential in seconden.</summary>
    public int TtlSeconds { get; set; } = 3600;

    /// <summary>TURN is pas actief als er zowel een geheim als een host is geconfigureerd.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(Secret) && !string.IsNullOrWhiteSpace(Host);
}
