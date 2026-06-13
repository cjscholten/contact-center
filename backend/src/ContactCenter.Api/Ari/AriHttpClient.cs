using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ContactCenter.Api.Ari;

/// <summary>De ARI-operaties die de gespreksflow gebruikt; geïmplementeerd door AriHttpClient,
/// gefaket in tests.</summary>
public interface IAriClient
{
    Task AnswerAsync(string channelId, CancellationToken ct = default);
    Task<string> PlayAsync(string channelId, string media, CancellationToken ct = default);
    Task ContinueInDialplanAsync(string channelId, string context, string extension, int priority,
        CancellationToken ct = default);
    Task HangupAsync(string channelId, CancellationToken ct = default);
    Task<string> CreateBridgeAsync(string type, CancellationToken ct = default);
    Task DestroyBridgeAsync(string bridgeId, CancellationToken ct = default);
    Task AddToBridgeAsync(string bridgeId, string channelId, CancellationToken ct = default);
    Task RemoveFromBridgeAsync(string bridgeId, string channelId, CancellationToken ct = default);
    Task StartBridgeMohAsync(string bridgeId, string mohClass = "default", CancellationToken ct = default);
    Task<string> OriginateToStasisAsync(string endpoint, string appArgs, string callerId,
        int timeoutSeconds = 30, CancellationToken ct = default);
}

public sealed class AriHttpClient(HttpClient http, IOptions<AriOptions> options) : IAriClient
{
    private readonly string _appName = options.Value.AppName;

    public Task AnswerAsync(string channelId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"channels/{Uri.EscapeDataString(channelId)}/answer", ct);

    public async Task<string> PlayAsync(string channelId, string media, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Post,
            $"channels/{Uri.EscapeDataString(channelId)}/play?media={Uri.EscapeDataString(media)}", ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public Task ContinueInDialplanAsync(string channelId, string context, string extension, int priority,
        CancellationToken ct = default)
        => SendAsync(HttpMethod.Post,
            $"channels/{Uri.EscapeDataString(channelId)}/continue" +
            $"?context={Uri.EscapeDataString(context)}&extension={Uri.EscapeDataString(extension)}&priority={priority}",
            ct);

    public Task HangupAsync(string channelId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"channels/{Uri.EscapeDataString(channelId)}", ct);

    // --- Bruggen --------------------------------------------------------------

    /// <summary>Maakt een brug (type "mixing" voor gesprek, "holding" voor wachtmuziek) en geeft het id terug.</summary>
    public async Task<string> CreateBridgeAsync(string type, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Post, $"bridges?type={Uri.EscapeDataString(type)}", ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public Task DestroyBridgeAsync(string bridgeId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"bridges/{Uri.EscapeDataString(bridgeId)}", ct);

    public Task AddToBridgeAsync(string bridgeId, string channelId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post,
            $"bridges/{Uri.EscapeDataString(bridgeId)}/addChannel?channel={Uri.EscapeDataString(channelId)}", ct);

    public Task RemoveFromBridgeAsync(string bridgeId, string channelId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post,
            $"bridges/{Uri.EscapeDataString(bridgeId)}/removeChannel?channel={Uri.EscapeDataString(channelId)}", ct);

    /// <summary>Start wachtmuziek op een holding-brug; alle deelnemers horen deze.</summary>
    public Task StartBridgeMohAsync(string bridgeId, string mohClass = "default", CancellationToken ct = default)
        => SendAsync(HttpMethod.Post,
            $"bridges/{Uri.EscapeDataString(bridgeId)}/moh?mohClass={Uri.EscapeDataString(mohClass)}", ct);

    // --- Originate ------------------------------------------------------------

    /// <summary>Belt een endpoint en levert het beantwoorde kanaal in de Stasis-app af. Geeft het kanaal-id terug.</summary>
    public async Task<string> OriginateToStasisAsync(
        string endpoint, string appArgs, string callerId, int timeoutSeconds = 30, CancellationToken ct = default)
    {
        var body = await SendAsync(HttpMethod.Post,
            $"channels?endpoint={Uri.EscapeDataString(endpoint)}" +
            $"&app={Uri.EscapeDataString(_appName)}" +
            $"&appArgs={Uri.EscapeDataString(appArgs)}" +
            $"&callerId={Uri.EscapeDataString(callerId)}" +
            $"&timeout={timeoutSeconds}", ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> SendAsync(HttpMethod method, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new AriRequestException(method, path, response.StatusCode, body);
        return body;
    }
}

public sealed class AriRequestException(HttpMethod method, string path, HttpStatusCode status, string body)
    : Exception($"ARI {method} {path} faalde met {(int)status}: {body}");
