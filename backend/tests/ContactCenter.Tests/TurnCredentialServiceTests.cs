using System.Security.Cryptography;
using System.Text;
using ContactCenter.Api.Turn;
using Microsoft.Extensions.Options;

namespace ContactCenter.Tests;

public class TurnCredentialServiceTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static TurnCredentialService Build(
        string secret, string host, int ttl = 3600, DateTimeOffset? now = null)
        => new(
            Options.Create(new TurnOptions { Secret = secret, Host = host, Port = 3478, TtlSeconds = ttl }),
            new FixedTime(now ?? DateTimeOffset.UtcNow));

    [Fact]
    public void Zonder_geheim_geen_ice_servers()
    {
        var sut = Build(secret: "", host: "20.107.0.204");
        Assert.Empty(sut.GetIceServers("agent1001"));
    }

    [Fact]
    public void Zonder_host_geen_ice_servers()
    {
        var sut = Build(secret: "s3cr3t", host: "");
        Assert.Empty(sut.GetIceServers("agent1001"));
    }

    [Fact]
    public void Geeft_stun_plus_turn_met_tijdelijke_credential()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
        var sut = Build(secret: "s3cr3t", host: "20.107.0.204", ttl: 3600, now: now);

        var servers = sut.GetIceServers("agent1001");

        Assert.Equal(2, servers.Count);
        Assert.Equal(new[] { "stun:20.107.0.204:3478" }, servers[0].Urls);

        var turn = servers[1];
        // username = "<vervaltijd>:<gebruiker>", vervaltijd = now + ttl
        Assert.Equal("1003600:agent1001", turn.Username);
        Assert.Contains("turn:20.107.0.204:3478?transport=udp", turn.Urls);
        Assert.Contains("turn:20.107.0.204:3478?transport=tcp", turn.Urls);

        // credential = base64(HMAC-SHA1(secret, username)) — moet coturn's use-auth-secret volgen.
        var expected = Convert.ToBase64String(
            new HMACSHA1(Encoding.UTF8.GetBytes("s3cr3t"))
                .ComputeHash(Encoding.UTF8.GetBytes("1003600:agent1001")));
        Assert.Equal(expected, turn.Credential);
    }

    [Fact]
    public void Verschillende_gebruikers_krijgen_verschillende_credentials()
    {
        var sut = Build(secret: "s3cr3t", host: "20.107.0.204");
        var a = sut.GetIceServers("agent1001")[1];
        var b = sut.GetIceServers("agent1002")[1];
        Assert.NotEqual(a.Username, b.Username);
        Assert.NotEqual(a.Credential, b.Credential);
    }
}
