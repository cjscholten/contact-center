namespace ContactCenter.Api.Agents;

public enum AgentStatus
{
    LoggedOut,
    Available,
    Ringing,  // gereserveerd voor een beller; originate loopt
    OnCall,
    WrapUp,
}

public sealed record AgentSnapshot(string Name, string DisplayName, AgentStatus Status, DateTimeOffset Since);
