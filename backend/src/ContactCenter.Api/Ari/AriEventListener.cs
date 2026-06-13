using System.Net.WebSockets;
using System.Text.Json;
using ContactCenter.Api.CallFlow;
using Microsoft.Extensions.Options;

namespace ContactCenter.Api.Ari;

public sealed class AriEventListener(
    IOptions<AriOptions> options,
    InboundCallHandler callHandler,
    CallCoordinator coordinator,
    ILogger<AriEventListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var eventsUri = BuildEventsUri(options.Value);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(eventsUri, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("ARI-eventverbinding verbroken ({Reden}), opnieuw verbinden over 3s",
                    ex.InnerException?.Message ?? ex.Message);
            }

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private static Uri BuildEventsUri(AriOptions opts)
    {
        var baseUri = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
        var scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        // subscribeAll: ook events van kanalen buiten de Stasis-app (de agent-legs),
        // nodig om gespreksbegin/-einde per agent te zien voor de statusmachine
        return new Uri($"{scheme}://{baseUri.Authority}{baseUri.AbsolutePath}events" +
                       $"?app={Uri.EscapeDataString(opts.AppName)}" +
                       $"&subscribeAll=true" +
                       $"&api_key={Uri.EscapeDataString($"{opts.Username}:{opts.Password}")}");
    }

    private async Task ListenAsync(Uri eventsUri, CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(eventsUri, ct);
        logger.LogInformation("Verbonden met ARI-events als applicatie '{App}'", options.Value.AppName);

        var buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                    return;
                }
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            message.Position = 0;
            using var json = await JsonDocument.ParseAsync(message, cancellationToken: ct);
            await DispatchAsync(json.RootElement, ct);
        }
    }

    private async Task DispatchAsync(JsonElement evt, CancellationToken ct)
    {
        var type = evt.GetProperty("type").GetString();
        try
        {
            switch (type)
            {
                case "StasisStart" when IsAgentLeg(evt):
                    // door de backend gebelde agent heeft opgenomen
                    await coordinator.OnAgentAnsweredAsync(ChannelId(evt), ct);
                    break;
                case "StasisStart":
                    await callHandler.OnStasisStartAsync(evt, ct);
                    break;
                case "PlaybackFinished":
                    await callHandler.OnPlaybackFinishedAsync(evt, ct);
                    break;
                case "ChannelDestroyed":
                    await coordinator.OnChannelGoneAsync(ChannelId(evt), ct);
                    break;
                case "StasisEnd":
                    callHandler.OnStasisEnd(evt);
                    break;
                default:
                    logger.LogDebug("ARI-event {Type} genegeerd", type);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fout bij verwerken van ARI-event {Type}", type);
        }
    }

    private static string ChannelId(JsonElement evt)
        => evt.GetProperty("channel").GetProperty("id").GetString()!;

    /// <summary>Agent-legs worden door de backend ge-originate met appArg "agent".</summary>
    private static bool IsAgentLeg(JsonElement evt)
        => evt.TryGetProperty("args", out var args)
           && args.ValueKind == JsonValueKind.Array
           && args.GetArrayLength() > 0
           && args[0].GetString() == "agent";
}
