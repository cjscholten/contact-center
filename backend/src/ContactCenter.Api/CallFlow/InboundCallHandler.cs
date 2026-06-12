using System.Collections.Concurrent;
using System.Text.Json;
using ContactCenter.Api.Ari;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.CallFlow;

public sealed class InboundCallHandler(
    AriHttpClient ari,
    IDbContextFactory<CcDbContext> dbFactory,
    QueueDecisionService decisions,
    ILogger<InboundCallHandler> logger)
{
    private const string QueueContext = "cc-queues";
    private const string ForwardContext = "cc-forward";
    private const string UnknownNumberPrompt = "sound:ss-noservice";

    // Terugval als de database onbereikbaar is: telefonie blijft werken.
    private static readonly RouteToQueue FallbackAction = new("support", "sound:queue-thankyou");

    private readonly ConcurrentDictionary<string, PendingAction> _afterPlayback = new();

    private sealed record PendingAction(string ChannelId, CallAction Action);

    public async Task OnStasisStartAsync(JsonElement evt, CancellationToken ct)
    {
        var channel = evt.GetProperty("channel");
        var channelId = channel.GetProperty("id").GetString()!;
        var caller = channel.GetProperty("caller").GetProperty("number").GetString();
        var dialed = channel.GetProperty("dialplan").GetProperty("exten").GetString()!;

        var action = await DecideAsync(dialed, ct);
        logger.LogInformation("Inkomend gesprek {ChannelId} van '{Caller}' naar '{Dialed}' → {Action}",
            channelId, caller, dialed, action);

        await ari.AnswerAsync(channelId, ct);

        switch (action)
        {
            case ForwardTo forward:
                await ari.ContinueInDialplanAsync(channelId, ForwardContext, forward.Number, 1, ct);
                break;
            case RouteToQueue route:
                await PlayThenAsync(channelId, route.WelcomePrompt, route, ct);
                break;
            case PlayAndHangup closed:
                await PlayThenAsync(channelId, closed.Prompt, closed, ct);
                break;
        }
    }

    public async Task OnPlaybackFinishedAsync(JsonElement evt, CancellationToken ct)
    {
        var playbackId = evt.GetProperty("playback").GetProperty("id").GetString()!;
        if (!_afterPlayback.TryRemove(playbackId, out var pending))
            return;

        switch (pending.Action)
        {
            case RouteToQueue route:
                logger.LogInformation("Kanaal {ChannelId} naar wachtrij '{Queue}'", pending.ChannelId, route.QueueName);
                // dialplan-prefix 'q' vermijdt botsing met speciale extensions (h/i/t)
                await ari.ContinueInDialplanAsync(pending.ChannelId, QueueContext, "q" + route.QueueName, 1, ct);
                break;
            case PlayAndHangup:
                logger.LogInformation("Kanaal {ChannelId} opgehangen na melding", pending.ChannelId);
                await ari.HangupAsync(pending.ChannelId, ct);
                break;
        }
    }

    public void OnStasisEnd(JsonElement evt)
    {
        var channelId = evt.GetProperty("channel").GetProperty("id").GetString()!;
        foreach (var stale in _afterPlayback.Where(p => p.Value.ChannelId == channelId).ToList())
            _afterPlayback.TryRemove(stale.Key, out _);
        logger.LogInformation("Kanaal {ChannelId} heeft de applicatie verlaten", channelId);
    }

    private async Task PlayThenAsync(string channelId, string prompt, CallAction action, CancellationToken ct)
    {
        var playbackId = await ari.PlayAsync(channelId, prompt, ct);
        _afterPlayback[playbackId] = new PendingAction(channelId, action);
    }

    private async Task<CallAction> DecideAsync(string dialed, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var queue = await db.Queues.AsNoTracking()
                .Include(q => q.OpeningHours)
                .Where(q => q.Numbers.Any(n => n.Number == dialed))
                .FirstOrDefaultAsync(ct);

            if (queue is null)
            {
                logger.LogWarning("Geen wachtrij geconfigureerd voor nummer '{Dialed}'", dialed);
                return new PlayAndHangup(UnknownNumberPrompt);
            }

            return decisions.Decide(queue, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Configuratie ophalen mislukt voor '{Dialed}'; terugval naar wachtrij '{Queue}'",
                dialed, FallbackAction.QueueName);
            return FallbackAction;
        }
    }
}
