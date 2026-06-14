using ContactCenter.Api.Agents;
using ContactCenter.Api.CallFlow;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContactCenter.Tests;

public class CallCoordinatorTests
{
    private static (CallCoordinator coordinator, FakeAriClient ari, AgentStateService agents) Build(int wrapUpSeconds)
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds, ("agent1001", ["support"]));
        var agents = new AgentStateService(factory, NullLogger<AgentStateService>.Instance);
        var ari = new FakeAriClient();
        var coordinator = new CallCoordinator(ari, agents, NullLogger<CallCoordinator>.Instance);
        return (coordinator, ari, agents);
    }

    [Fact]
    public async Task Wachtende_beller_komt_in_holding_brug_met_wachtmuziek()
    {
        var (coordinator, ari, _) = Build(wrapUpSeconds: 0);

        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");

        Assert.Contains(ari.BridgeTypes, b => b.Value == "holding");
        var holding = ari.BridgeTypes.First(b => b.Value == "holding").Key;
        Assert.Contains(holding, ari.MohStarted);
        Assert.Contains((holding, "caller-1"), ari.Added);
    }

    [Fact]
    public async Task Beschikbare_agent_wordt_gebeld_voor_wachtende_beller()
    {
        var (coordinator, ari, agents) = Build(wrapUpSeconds: 0);
        await agents.LoginAsync("agent1001");

        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.TryDispatchAllAsync();

        var originate = Assert.Single(ari.Originates);
        Assert.Equal("PJSIP/agent1001", originate.Endpoint);
        Assert.Equal("agent", originate.AppArgs);
        Assert.Equal(AgentStatus.Ringing, (await agents.GetAsync("agent1001"))!.Status);
    }

    [Fact]
    public async Task Geen_beschikbare_agent_laat_beller_wachten()
    {
        var (coordinator, ari, _) = Build(wrapUpSeconds: 0); // niemand ingelogd

        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.TryDispatchAllAsync();

        Assert.Empty(ari.Originates);
    }

    [Fact]
    public async Task Opnemen_zet_beller_en_agent_in_een_mixing_brug()
    {
        var (coordinator, ari, agents) = Build(wrapUpSeconds: 0);
        await agents.LoginAsync("agent1001");
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.TryDispatchAllAsync();
        var agentChannel = ari.Originates.Single().ChannelId;

        await coordinator.OnAgentAnsweredAsync(agentChannel);

        Assert.Contains(ari.BridgeTypes, b => b.Value == "mixing");
        var mixing = ari.BridgeTypes.First(b => b.Value == "mixing").Key;
        Assert.Contains((mixing, "caller-1"), ari.Added);
        Assert.Contains((mixing, agentChannel), ari.Added);
        Assert.Equal(AgentStatus.OnCall, (await agents.GetAsync("agent1001"))!.Status);
    }

    [Fact]
    public async Task Beller_ophangen_ruimt_gesprek_op_en_start_nawerktijd()
    {
        var (coordinator, ari, agents) = Build(wrapUpSeconds: 30);
        await agents.LoginAsync("agent1001");
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.TryDispatchAllAsync();
        var agentChannel = ari.Originates.Single().ChannelId;
        await coordinator.OnAgentAnsweredAsync(agentChannel);

        await coordinator.OnChannelGoneAsync("caller-1");

        Assert.Contains(agentChannel, ari.Hangups); // agent-leg opgehangen
        Assert.NotEmpty(ari.DestroyedBridges);       // mixing-brug opgeruimd
        Assert.Equal(AgentStatus.WrapUp, (await agents.GetAsync("agent1001"))!.Status);
    }

    private static async Task<(CallCoordinator coordinator, FakeAriClient ari, AgentStateService agents, string agentChannel)>
        ActiveCallAsync(int wrapUpSeconds = 0)
    {
        var (coordinator, ari, agents) = Build(wrapUpSeconds);
        await agents.LoginAsync("agent1001");
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.TryDispatchAllAsync();
        var agentChannel = ari.Originates.Single().ChannelId;
        await coordinator.OnAgentAnsweredAsync(agentChannel);
        return (coordinator, ari, agents, agentChannel);
    }

    [Fact]
    public async Task In_de_wacht_verplaatst_beller_naar_de_holding_brug()
    {
        var (coordinator, ari, _, _) = await ActiveCallAsync();
        ari.Added.Clear();

        Assert.True(await coordinator.HoldAsync("agent1001"));

        var holding = ari.BridgeTypes.First(b => b.Value == "holding").Key;
        Assert.Contains((holding, "caller-1"), ari.Added);
    }

    [Fact]
    public async Task Uit_de_wacht_zet_beller_terug_in_de_mixing_brug()
    {
        var (coordinator, ari, _, _) = await ActiveCallAsync();
        await coordinator.HoldAsync("agent1001");
        ari.Added.Clear();

        Assert.True(await coordinator.UnholdAsync("agent1001"));

        var mixing = ari.BridgeTypes.First(b => b.Value == "mixing").Key;
        Assert.Contains((mixing, "caller-1"), ari.Added);
    }

    [Fact]
    public async Task Dubbel_in_de_wacht_zetten_doet_niets()
    {
        var (coordinator, _, _, _) = await ActiveCallAsync();
        Assert.True(await coordinator.HoldAsync("agent1001"));
        Assert.False(await coordinator.HoldAsync("agent1001")); // al in de wacht
    }

    [Fact]
    public async Task Hold_zonder_actief_gesprek_doet_niets()
    {
        var (coordinator, _, agents) = Build(wrapUpSeconds: 0);
        await agents.LoginAsync("agent1001");
        Assert.False(await coordinator.HoldAsync("agent1001"));
    }

    [Fact]
    public async Task Agent_die_niet_opneemt_geeft_de_beller_terug_aan_de_wacht()
    {
        var (coordinator, ari, agents) = Build(wrapUpSeconds: 0);
        await agents.LoginAsync("agent1001");
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.TryDispatchAllAsync();
        var agentChannel = ari.Originates.Single().ChannelId;

        // agent-leg verdwijnt zonder op te nemen
        await coordinator.OnChannelGoneAsync(agentChannel);
        Assert.Equal(AgentStatus.Available, (await agents.GetAsync("agent1001"))!.Status);

        // en kan opnieuw gedispatcht worden
        await coordinator.TryDispatchAllAsync();
        Assert.Equal(2, ari.Originates.Count);
    }
}
