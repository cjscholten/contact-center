namespace ContactCenter.Api.Data;

public class Agent
{
    public int Id { get; set; }

    /// <summary>Eigenaar (klant) van deze agent. Zie <see cref="Tenant"/>.</summary>
    public int TenantId { get; set; }

    /// <summary>Loginnaam (uniek binnen de tenant); gelijk aan de SIP-gebruikersnaam zolang er geen IdP-koppeling is.</summary>
    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>PJSIP-endpoint van de agent, bv. "PJSIP/agent1001".</summary>
    public required string Endpoint { get; set; }

    /// <summary>SIP-wachtwoord voor de WebRTC-registratie; uitgegeven via /api/agents/me/sip
    /// na Keycloak-login. Dev: gelijk aan de waarde in pjsip.conf.</summary>
    public string SipPassword { get; set; } = "changeme-dev";

    public List<AgentQueueAssignment> QueueAssignments { get; set; } = [];
}
