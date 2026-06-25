using ContactCenter.Api.Admin;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Tests;

public class AdminAgentsTests
{
    private static AgentWriteRequest Req(
        string name = "agent2001",
        string display = "Agent 2001",
        string endpoint = "PJSIP/agent2001",
        IReadOnlyList<int>? queueIds = null)
        => new(name, display, endpoint, queueIds ?? []);

    /// <summary>Maakt een factory met twee wachtrijen en geeft hun ids terug.</summary>
    private static async Task<(TestDbContextFactory factory, int supportId, int salesId)> WithQueuesAsync()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var support = new QueueConfig { Name = "support", DisplayName = "Support" };
        var sales = new QueueConfig { Name = "sales", DisplayName = "Sales" };
        db.Queues.AddRange(support, sales);
        await db.SaveChangesAsync();
        return (factory, support.Id, sales.Id);
    }

    [Fact]
    public async Task Create_geldige_agent_slaat_wachtrij_toewijzingen_op()
    {
        var (factory, supportId, salesId) = await WithQueuesAsync();
        await using var db = factory.CreateDbContext();

        var result = await AdminApi.CreateAgentAsync(db, Req(queueIds: [supportId, salesId]));

        Assert.Null(result.Error);
        Assert.Equal("agent2001", result.Detail!.Name);
        Assert.Equal(2, result.Detail.QueueIds.Count);

        await using var verify = factory.CreateDbContext();
        var saved = await verify.Agents.Include(a => a.QueueAssignments).SingleAsync(a => a.Name == "agent2001");
        Assert.Equal(2, saved.QueueAssignments.Count);
    }

    [Fact]
    public async Task Create_met_ongeldige_naam_faalt()
    {
        var (factory, _, _) = await WithQueuesAsync();
        await using var db = factory.CreateDbContext();
        var result = await AdminApi.CreateAgentAsync(db, Req(name: "agent 2001")); // spatie
        Assert.NotNull(result.Error);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task Create_met_dubbele_naam_faalt()
    {
        var (factory, supportId, _) = await WithQueuesAsync();
        await using var db = factory.CreateDbContext();
        await AdminApi.CreateAgentAsync(db, Req(queueIds: [supportId]));
        var result = await AdminApi.CreateAgentAsync(db, Req(queueIds: [supportId]));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Create_zonder_endpoint_faalt()
    {
        var (factory, _, _) = await WithQueuesAsync();
        await using var db = factory.CreateDbContext();
        var result = await AdminApi.CreateAgentAsync(db, Req(endpoint: "  "));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Create_met_onbekende_wachtrij_faalt()
    {
        var (factory, _, _) = await WithQueuesAsync();
        await using var db = factory.CreateDbContext();
        var result = await AdminApi.CreateAgentAsync(db, Req(queueIds: [9999]));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Update_vervangt_wachtrij_toewijzingen()
    {
        var (factory, supportId, salesId) = await WithQueuesAsync();
        await using var db = factory.CreateDbContext();
        var created = await AdminApi.CreateAgentAsync(db, Req(queueIds: [supportId]));
        var id = created.Detail!.Id;

        var result = await AdminApi.UpdateAgentAsync(db, id, Req(display: "Gewijzigd", queueIds: [salesId]));

        Assert.NotNull(result);
        Assert.Null(result!.Error);
        Assert.Equal("Gewijzigd", result.Detail!.DisplayName);
        Assert.Equal(salesId, Assert.Single(result.Detail.QueueIds));

        await using var verify = factory.CreateDbContext();
        var saved = await verify.Agents.Include(a => a.QueueAssignments).SingleAsync(a => a.Id == id);
        Assert.Equal(salesId, Assert.Single(saved.QueueAssignments).QueueConfigId); // support-toewijzing verwijderd
    }

    [Fact]
    public async Task Update_van_onbekende_agent_geeft_null()
    {
        var (factory, _, _) = await WithQueuesAsync();
        await using var db = factory.CreateDbContext();
        Assert.Null(await AdminApi.UpdateAgentAsync(db, 999, Req()));
    }

    [Fact]
    public async Task Update_laat_de_naam_ongemoeid()
    {
        var (factory, supportId, _) = await WithQueuesAsync();
        await using var db = factory.CreateDbContext();
        var created = await AdminApi.CreateAgentAsync(db, Req(name: "agent2001", queueIds: [supportId]));
        var id = created.Detail!.Id;

        var result = await AdminApi.UpdateAgentAsync(db, id, Req(name: "ietsanders", display: "Nieuw", queueIds: [supportId]));

        Assert.NotNull(result);
        Assert.Null(result!.Error);
        Assert.Equal("agent2001", result.Detail!.Name); // read-only bij wijzigen
        Assert.Equal("Nieuw", result.Detail.DisplayName);
    }
}
