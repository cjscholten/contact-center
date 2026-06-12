namespace ContactCenter.Api.Data;

public class Agent
{
    public int Id { get; set; }

    /// <summary>Loginnaam; gelijk aan de SIP-gebruikersnaam zolang er geen IdP-koppeling is.</summary>
    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>PJSIP-endpoint van de agent, bv. "PJSIP/agent1001".</summary>
    public required string Endpoint { get; set; }

    public List<AgentQueueAssignment> QueueAssignments { get; set; } = [];
}
