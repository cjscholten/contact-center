using System.Net;
using System.Text.Json;

namespace ContactCenter.Api.Ari;

public sealed class AriHttpClient(HttpClient http)
{
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
