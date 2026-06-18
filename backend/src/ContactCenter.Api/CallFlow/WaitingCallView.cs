namespace ContactCenter.Api.CallFlow;

/// <summary>Een nog niet aangenomen, wachtend gesprek — zoals getoond in het wachtrij-overzicht.</summary>
public sealed record WaitingCallView(
    string CallId,
    string QueueName,
    string CallerNumber,
    DateTimeOffset EnqueuedAt);
