using System.Collections.Concurrent;
using System.Text.Json;
using ContactCenter.Api.Ari;

namespace ContactCenter.Api.CallFlow;

public sealed class InboundCallHandler(AriHttpClient ari, ILogger<InboundCallHandler> logger)
{
    private const string WelcomePrompt = "sound:queue-thankyou";
    private const string QueueContext = "cc-queues";
    private const string DefaultQueue = "support";

    private readonly ConcurrentDictionary<string, string> _playbackToChannel = new();

    public async Task OnStasisStartAsync(JsonElement evt, CancellationToken ct)
    {
        var channel = evt.GetProperty("channel");
        var channelId = channel.GetProperty("id").GetString()!;
        var caller = channel.GetProperty("caller").GetProperty("number").GetString();
        logger.LogInformation("Inkomend gesprek {ChannelId} van '{Caller}'", channelId, caller);

        await ari.AnswerAsync(channelId, ct);
        var playbackId = await ari.PlayAsync(channelId, WelcomePrompt, ct);
        _playbackToChannel[playbackId] = channelId;
    }

    public async Task OnPlaybackFinishedAsync(JsonElement evt, CancellationToken ct)
    {
        var playbackId = evt.GetProperty("playback").GetProperty("id").GetString()!;
        if (!_playbackToChannel.TryRemove(playbackId, out var channelId))
            return;

        logger.LogInformation("Welkomsttekst klaar, kanaal {ChannelId} naar wachtrij '{Queue}'",
            channelId, DefaultQueue);
        await ari.ContinueInDialplanAsync(channelId, QueueContext, DefaultQueue, 1, ct);
    }

    public void OnStasisEnd(JsonElement evt)
    {
        var channelId = evt.GetProperty("channel").GetProperty("id").GetString()!;
        foreach (var stale in _playbackToChannel.Where(p => p.Value == channelId).ToList())
            _playbackToChannel.TryRemove(stale.Key, out _);
        logger.LogInformation("Kanaal {ChannelId} heeft de applicatie verlaten", channelId);
    }
}
