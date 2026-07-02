namespace ContactCenter.Api.Agents;

/// <summary>Systeem-bepaalde gespreksfase van de agent.</summary>
public enum AgentStatus
{
    LoggedOut,
    Available,
    Ringing,  // gereserveerd voor een beller; originate loopt
    OnCall,
    WrapUp,
}

/// <summary>Handmatig door de agent gekozen beschikbaarheid.</summary>
public enum Presence
{
    Available,
    Break,
    Unavailable,
}

public sealed record AgentSnapshot(
    string Name,
    string DisplayName,
    AgentStatus Status,
    Presence Presence,
    DateTimeOffset Since,
    // Wanneer de nawerktijd automatisch afloopt (alleen gevuld bij status WrapUp); voor de aftelklok in de UI.
    DateTimeOffset? WrapUpEndsAt = null);
