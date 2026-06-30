namespace ContactCenter.Api.CallFlow;

public abstract record CallAction;

/// <summary>Welkomsttekst afspelen en daarna in de wachtrij (van deze tenant) plaatsen.</summary>
public sealed record RouteToQueue(int TenantId, string QueueName, string WelcomePrompt) : CallAction;

/// <summary>Tekst afspelen (bv. gesloten-melding) en ophangen.</summary>
public sealed record PlayAndHangup(string Prompt) : CallAction;

/// <summary>Direct doorschakelen naar een extern nummer via de trunk.</summary>
public sealed record ForwardTo(string Number) : CallAction;
