using System.Collections.Concurrent;
using System.Text.Json;
using ContactCenter.Api.Ari;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.CallFlow;

/// <summary>
/// Handelt de inkomende beller af tot en met de keuze: welkomsttekst + in de wacht (via de
/// CallCoordinator), gesloten-melding, of doorschakelen. Vanaf het moment dat de beller in de
/// wacht staat neemt de CallCoordinator het over.
/// </summary>
public sealed class InboundCallHandler(
    IAriClient ari,
    IDbContextFactory<CcDbContext> dbFactory,
    QueueDecisionService decisions,
    CallCoordinator coordinator,
    ILogger<InboundCallHandler> logger)
{
    private const string ForwardContext = "cc-forward";
    private const string UnknownNumberPrompt = "sound:ss-noservice";

    // Terugval als de database onbereikbaar is: telefonie blijft werken.
    private static readonly RouteToQueue FallbackAction = new("support", "sound:queue-thankyou");

    private readonly ConcurrentDictionary<string, PendingAction> _afterPlayback = new();

    private sealed record PendingAction(string ChannelId, CallAction Action, string CallerId);

    public async Task OnStasisStartAsync(JsonElement evt, CancellationToken ct)
    {
        var channel = evt.GetProperty("channel");
        var channelId = channel.GetProperty("id").GetString()!;
        var caller = channel.GetProperty("caller").GetProperty("number").GetString() ?? "";
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
                await PlayThenAsync(channelId, route.WelcomePrompt, route, caller, ct);
                break;
            case PlayAndHangup closed:
                await PlayThenAsync(channelId, closed.Prompt, closed, caller, ct);
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
                await coordinator.EnqueueCallerAsync(pending.ChannelId, route.QueueName, pending.CallerId, ct);
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
    }

    private async Task PlayThenAsync(string channelId, string prompt, CallAction action, string callerId,
        CancellationToken ct)
    {
        var playbackId = await ari.PlayAsync(channelId, prompt, ct);
        _afterPlayback[playbackId] = new PendingAction(channelId, action, callerId);
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
