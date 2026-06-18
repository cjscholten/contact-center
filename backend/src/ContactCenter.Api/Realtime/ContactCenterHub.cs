using Microsoft.AspNetCore.SignalR;

namespace ContactCenter.Api.Realtime;

/// <summary>
/// SignalR-hub voor de agent-schermen. Clients luisteren op "queuesChanged" (en later op
/// agent-status). De initiële stand halen ze via GET /api/queues; updates komen via deze hub.
/// </summary>
public sealed class ContactCenterHub : Hub;
