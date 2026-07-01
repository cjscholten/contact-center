using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ContactCenter.Api.Turn;

/// <summary>Eén ICE-server-item voor de WebRTC-RTCPeerConnection (vorm = RTCIceServer in de browser).</summary>
public sealed record IceServer(string[] Urls, string? Username = null, string? Credential = null);

/// <summary>
/// Geeft tijdelijke TURN-credentials uit volgens coturn's "REST API"/use-auth-secret-schema:
/// username = "<unix-vervaltijd>:<gebruiker>", credential = base64(HMAC-SHA1(secret, username)).
/// De browser krijgt hiermee een korte-levensduur-credential; het geheim blijft server-side.
/// </summary>
public sealed class TurnCredentialService(IOptions<TurnOptions> options, TimeProvider time)
{
    private readonly TurnOptions _options = options.Value;
    private readonly TimeProvider _time = time;

    /// <summary>
    /// ICE-servers voor de opgegeven gebruiker. Leeg wanneer TURN niet is geconfigureerd, zodat de
    /// softphone gewoon terugvalt op host-kandidaten (lokaal netwerk werkt dan nog steeds).
    /// </summary>
    public IReadOnlyList<IceServer> GetIceServers(string user)
    {
        if (!_options.Enabled) return [];

        var expiry = _time.GetUtcNow().ToUnixTimeSeconds() + _options.TtlSeconds;
        var username = $"{expiry}:{user}";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_options.Secret));
        var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));

        var host = _options.Host;
        var port = _options.Port;
        return
        [
            new IceServer([$"stun:{host}:{port}"]),
            new IceServer(
                [$"turn:{host}:{port}?transport=udp", $"turn:{host}:{port}?transport=tcp"],
                username,
                credential),
        ];
    }
}
