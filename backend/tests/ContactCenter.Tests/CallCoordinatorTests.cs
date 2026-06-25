using ContactCenter.Api.Agents;
using ContactCenter.Api.CallFlow;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContactCenter.Tests;

public class CallCoordinatorTests
{
    private static (CallCoordinator coordinator, FakeAriClient ari, AgentStateService agents) Build(
        int wrapUpSeconds, params (string name, string[] queues)[] agents)
    {
        if (agents.Length == 0)
            agents = [("agent1001", ["support"])];
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds, agents);
        var agentSvc = new AgentStateService(factory, NullLogger<AgentStateService>.Instance);
        var ari = new FakeAriClient();
        var coordinator = new CallCoordinator(
            ari, agentSvc, factory, new FakeRealtimeNotifier(), NullLogger<CallCoordinator>.Instance);
        return (coordinator, ari, agentSvc);
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
    public async Task Pickup_neemt_een_specifiek_wachtend_gesprek_aan()
    {
        var (coordinator, ari, agents) = Build(wrapUpSeconds: 0);
        await agents.LoginAsync("agent1001");
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");

        Assert.True(await coordinator.PickupAsync("agent1001", "caller-1"));

        var originate = Assert.Single(ari.Originates);
        Assert.Equal("PJSIP/agent1001", originate.Endpoint);
    }

    [Fact]
    public async Task Pickup_van_onbekend_gesprek_faalt()
    {
        var (coordinator, _, agents) = Build(wrapUpSeconds: 0);
        await agents.LoginAsync("agent1001");
        Assert.False(await coordinator.PickupAsync("agent1001", "bestaat-niet"));
    }

    [Fact]
    public async Task Tweede_pickup_van_hetzelfde_gesprek_faalt()
    {
        var (coordinator, _, agents) =
            Build(wrapUpSeconds: 0, ("agent1001", ["support"]), ("agent1002", ["support"]));
        await agents.LoginAsync("agent1001");
        await agents.LoginAsync("agent1002");
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");

        Assert.True(await coordinator.PickupAsync("agent1001", "caller-1"));
        Assert.False(await coordinator.PickupAsync("agent1002", "caller-1")); // al uit de wacht
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

    [Fact]
    public async Task Doorverbinden_naar_collega_belt_die_agent_en_zet_huidige_in_nawerktijd()
    {
        var (coordinator, ari, agents) =
            Build(wrapUpSeconds: 30, ("agent1001", ["support"]), ("agent1002", ["support"]));
        await agents.LoginAsync("agent1001");
        await agents.LoginAsync("agent1002");
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.PickupAsync("agent1001", "caller-1");
        var firstAgentChannel = ari.Originates.Single().ChannelId;
        await coordinator.OnAgentAnsweredAsync(firstAgentChannel);
        ari.Originates.Clear();

        Assert.True(await coordinator.TransferToAgentAsync("agent1001", "agent1002"));

        var originate = Assert.Single(ari.Originates);
        Assert.Equal("PJSIP/agent1002", originate.Endpoint);
        Assert.Contains(firstAgentChannel, ari.Hangups);
        Assert.Equal(AgentStatus.WrapUp, (await agents.GetAsync("agent1001"))!.Status);
    }

    [Fact]
    public async Task Doorverbinden_naar_niet_ingelogde_agent_faalt()
    {
        var (coordinator, _, _, _) = await ActiveCallAsync();
        Assert.False(await coordinator.TransferToAgentAsync("agent1001", "agent1002"));
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
    public async Task Koud_doorverbinden_naar_wachtrij_herrouteert_beller_en_zet_agent_in_nawerktijd()
    {
        var (coordinator, ari, agents) = Build(30, ("agent1001", ["support"]), ("agent1002", ["sales"]));
        await agents.LoginAsync("agent1001"); // agent1002 niet ingelogd → beller blijft in sales wachten
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.TryDispatchAllAsync();
        var agentChannel = ari.Originates.Single().ChannelId;
        await coordinator.OnAgentAnsweredAsync(agentChannel);
        ari.Added.Clear();

        Assert.True(await coordinator.ColdTransferAsync("agent1001", "sales"));

        Assert.Contains(agentChannel, ari.Hangups);                         // agent-leg eruit
        Assert.Contains(ari.Added, a => a.Channel == "caller-1");           // beller naar holding van sales
        Assert.Equal(AgentStatus.WrapUp, (await agents.GetAsync("agent1001"))!.Status);
    }

    [Fact]
    public async Task Koud_doorverbinden_naar_extern_nummer_gaat_via_cc_forward()
    {
        var (coordinator, ari, _, _) = await ActiveCallAsync();

        Assert.True(await coordinator.ColdTransferAsync("agent1001", "+31201234567"));

        Assert.Contains(ari.Continued, c => c.Channel == "caller-1"
            && c.Context == "cc-forward" && c.Extension == "+31201234567");
    }

    [Fact]
    public async Task Koud_doorverbinden_zonder_actief_gesprek_doet_niets()
    {
        var (coordinator, _, agents) = Build(wrapUpSeconds: 0);
        await agents.LoginAsync("agent1001");
        Assert.False(await coordinator.ColdTransferAsync("agent1001", "sales"));
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

    // --- Warm doorverbinden (overleg) -----------------------------------------

    /// <summary>agent1001 in gesprek met caller-1; agent1002 ingelogd en beschikbaar. Originates leeg.</summary>
    private static async Task<(CallCoordinator coordinator, FakeAriClient ari, AgentStateService agents, string fromChannel)>
        ActiveCallWithColleagueAsync(int wrapUpSeconds = 0)
    {
        var (coordinator, ari, agents) =
            Build(wrapUpSeconds, ("agent1001", ["support"]), ("agent1002", ["support"]));
        await agents.LoginAsync("agent1001");
        await agents.LoginAsync("agent1002");
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.PickupAsync("agent1001", "caller-1");
        var fromChannel = ari.Originates.Single().ChannelId;
        await coordinator.OnAgentAnsweredAsync(fromChannel);
        ari.Originates.Clear();
        return (coordinator, ari, agents, fromChannel);
    }

    [Fact]
    public async Task Warm_doorverbinden_belt_de_collega_en_zet_de_beller_in_de_wacht()
    {
        var (coordinator, ari, agents, _) = await ActiveCallWithColleagueAsync();
        ari.Added.Clear();

        Assert.True(await coordinator.StartWarmTransferAsync("agent1001", "agent1002"));

        var originate = Assert.Single(ari.Originates);
        Assert.Equal("PJSIP/agent1002", originate.Endpoint);
        Assert.Equal("consult", originate.AppArgs);
        var holding = ari.BridgeTypes.First(b => b.Value == "holding").Key;
        Assert.Contains((holding, "caller-1"), ari.Added);                              // beller geparkeerd
        Assert.Equal(AgentStatus.OnCall, (await agents.GetAsync("agent1001"))!.Status);  // doorverbinder blijft in gesprek
        Assert.Equal(AgentStatus.Ringing, (await agents.GetAsync("agent1002"))!.Status); // collega wordt gebeld
    }

    [Fact]
    public async Task Overleg_aannemen_zet_de_collega_in_de_overlegbrug()
    {
        var (coordinator, ari, agents, _) = await ActiveCallWithColleagueAsync();
        await coordinator.StartWarmTransferAsync("agent1001", "agent1002");
        var consultChannel = ari.Originates.Single(o => o.AppArgs == "consult").ChannelId;
        var bridge = ari.BridgeTypes.First(b => b.Value == "mixing").Key; // overlegbrug = hergebruikte mixing-brug
        ari.Added.Clear();

        await coordinator.OnConsultAnsweredAsync(consultChannel);

        Assert.Contains((bridge, consultChannel), ari.Added); // collega bij de doorverbinder in de brug
        Assert.Equal(AgentStatus.OnCall, (await agents.GetAsync("agent1002"))!.Status);
    }

    [Fact]
    public async Task Overleg_voltooien_verbindt_de_beller_met_de_collega()
    {
        var (coordinator, ari, agents, fromChannel) = await ActiveCallWithColleagueAsync(wrapUpSeconds: 30);
        await coordinator.StartWarmTransferAsync("agent1001", "agent1002");
        var consultChannel = ari.Originates.Single(o => o.AppArgs == "consult").ChannelId;
        await coordinator.OnConsultAnsweredAsync(consultChannel);
        var bridge = ari.BridgeTypes.First(b => b.Value == "mixing").Key;
        ari.Added.Clear();

        Assert.True(await coordinator.CompleteWarmTransferAsync("agent1001"));

        Assert.Contains(fromChannel, ari.Hangups);                                       // doorverbinder eruit
        Assert.Contains((bridge, "caller-1"), ari.Added);                                // beller bij de collega
        Assert.Equal(AgentStatus.WrapUp, (await agents.GetAsync("agent1001"))!.Status);
        Assert.Equal(AgentStatus.OnCall, (await agents.GetAsync("agent1002"))!.Status);

        // de collega heeft nu het gesprek: ophangen door de beller zet hém in nawerktijd
        await coordinator.OnChannelGoneAsync("caller-1");
        Assert.Contains(consultChannel, ari.Hangups);
        Assert.Equal(AgentStatus.WrapUp, (await agents.GetAsync("agent1002"))!.Status);
    }

    [Fact]
    public async Task Overleg_annuleren_haalt_de_beller_terug_bij_de_agent()
    {
        var (coordinator, ari, agents, _) = await ActiveCallWithColleagueAsync();
        await coordinator.StartWarmTransferAsync("agent1001", "agent1002");
        var consultChannel = ari.Originates.Single(o => o.AppArgs == "consult").ChannelId;
        await coordinator.OnConsultAnsweredAsync(consultChannel);
        var bridge = ari.BridgeTypes.First(b => b.Value == "mixing").Key;
        ari.Added.Clear();

        Assert.True(await coordinator.CancelWarmTransferAsync("agent1001"));

        Assert.Contains(consultChannel, ari.Hangups);                                      // collega eruit
        Assert.Contains((bridge, "caller-1"), ari.Added);                                  // beller terug bij agent1001
        Assert.Equal(AgentStatus.Available, (await agents.GetAsync("agent1002"))!.Status); // collega vrij
        Assert.Equal(AgentStatus.OnCall, (await agents.GetAsync("agent1001"))!.Status);
        Assert.True(await coordinator.HoldAsync("agent1001")); // gesprek is weer actief en bedienbaar
    }

    [Fact]
    public async Task Warm_doorverbinden_naar_bezette_collega_faalt_en_laat_het_gesprek_intact()
    {
        var (coordinator, ari, agents) = Build(0, ("agent1001", ["support"]), ("agent1002", ["support"]));
        await agents.LoginAsync("agent1001"); // agent1002 niet ingelogd
        await coordinator.EnqueueCallerAsync("caller-1", "support", "+31600000000");
        await coordinator.PickupAsync("agent1001", "caller-1");
        await coordinator.OnAgentAnsweredAsync(ari.Originates.Single().ChannelId);
        ari.Originates.Clear();

        Assert.False(await coordinator.StartWarmTransferAsync("agent1001", "agent1002"));
        Assert.Empty(ari.Originates);                          // geen overlegleg gebeld
        Assert.True(await coordinator.HoldAsync("agent1001")); // oorspronkelijke gesprek nog intact
    }

    [Fact]
    public async Task Collega_neemt_overleg_niet_aan_geeft_de_beller_terug()
    {
        var (coordinator, ari, agents, _) = await ActiveCallWithColleagueAsync();
        await coordinator.StartWarmTransferAsync("agent1001", "agent1002");
        var consultChannel = ari.Originates.Single(o => o.AppArgs == "consult").ChannelId;
        var bridge = ari.BridgeTypes.First(b => b.Value == "mixing").Key;
        ari.Added.Clear();

        await coordinator.OnChannelGoneAsync(consultChannel); // collega-leg verdwijnt zonder op te nemen

        Assert.Contains((bridge, "caller-1"), ari.Added);                                  // beller terug bij agent1001
        Assert.Equal(AgentStatus.Available, (await agents.GetAsync("agent1002"))!.Status); // collega vrij
        Assert.True(await coordinator.HoldAsync("agent1001")); // gesprek weer actief
    }

    [Fact]
    public async Task Beller_hangt_op_tijdens_overleg_ruimt_beide_legs_op()
    {
        var (coordinator, ari, agents, fromChannel) = await ActiveCallWithColleagueAsync(wrapUpSeconds: 30);
        await coordinator.StartWarmTransferAsync("agent1001", "agent1002");
        var consultChannel = ari.Originates.Single(o => o.AppArgs == "consult").ChannelId;
        await coordinator.OnConsultAnsweredAsync(consultChannel);

        await coordinator.OnChannelGoneAsync("caller-1");

        Assert.Contains(fromChannel, ari.Hangups);     // doorverbinder eruit
        Assert.Contains(consultChannel, ari.Hangups);  // collega eruit
        Assert.Equal(AgentStatus.WrapUp, (await agents.GetAsync("agent1001"))!.Status);
        Assert.Equal(AgentStatus.Available, (await agents.GetAsync("agent1002"))!.Status);
    }

    [Fact]
    public async Task Doorverbinder_verbreekt_tijdens_overleg_draagt_de_beller_over_aan_de_collega()
    {
        var (coordinator, ari, agents, fromChannel) = await ActiveCallWithColleagueAsync(wrapUpSeconds: 30);
        await coordinator.StartWarmTransferAsync("agent1001", "agent1002");
        var consultChannel = ari.Originates.Single(o => o.AppArgs == "consult").ChannelId;
        await coordinator.OnConsultAnsweredAsync(consultChannel);
        var bridge = ari.BridgeTypes.First(b => b.Value == "mixing").Key;
        ari.Added.Clear();

        await coordinator.OnChannelGoneAsync(fromChannel); // doorverbinder verbreekt zelf

        Assert.Contains((bridge, "caller-1"), ari.Added); // beller overgedragen aan de collega
        Assert.Equal(AgentStatus.WrapUp, (await agents.GetAsync("agent1001"))!.Status);
        Assert.Equal(AgentStatus.OnCall, (await agents.GetAsync("agent1002"))!.Status);
    }

    [Fact]
    public async Task Overleg_voltooien_voordat_de_collega_opnam_faalt()
    {
        var (coordinator, ari, _, _) = await ActiveCallWithColleagueAsync();
        await coordinator.StartWarmTransferAsync("agent1001", "agent1002");
        Assert.False(await coordinator.CompleteWarmTransferAsync("agent1001")); // collega heeft nog niet opgenomen
    }

    [Fact]
    public async Task Warm_doorverbinden_zonder_actief_gesprek_doet_niets()
    {
        var (coordinator, _, agents) = Build(0, ("agent1001", ["support"]), ("agent1002", ["support"]));
        await agents.LoginAsync("agent1001");
        await agents.LoginAsync("agent1002");
        Assert.False(await coordinator.StartWarmTransferAsync("agent1001", "agent1002"));
    }
}
