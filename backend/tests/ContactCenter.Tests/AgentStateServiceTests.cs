using ContactCenter.Api.Agents;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContactCenter.Tests;

public class AgentStateServiceTests
{
    private static AgentStateService Build(TestDbContextFactory factory)
        => new(factory, NullLogger<AgentStateService>.Instance);

    [Fact]
    public async Task Reserveert_een_beschikbare_agent_in_de_wachtrij()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");

        var reserved = await sut.TryReserveForCallAsync(["support"]);

        Assert.NotNull(reserved);
        Assert.Equal("agent1001", reserved!.Name);
        Assert.Equal("PJSIP/agent1001", reserved.Endpoint);
    }

    [Fact]
    public async Task Reserveert_elke_agent_hooguit_een_keer()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]), ("agent1002", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");
        await sut.LoginAsync("agent1002");

        var first = await sut.TryReserveForCallAsync(["support"]);
        var second = await sut.TryReserveForCallAsync(["support"]);
        var third = await sut.TryReserveForCallAsync(["support"]);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.Name, second!.Name);
        Assert.Null(third); // beide nu Ringing
    }

    [Fact]
    public async Task Agent_buiten_de_wachtrij_wordt_niet_gereserveerd()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");

        Assert.Null(await sut.TryReserveForCallAsync(["sales"]));
    }

    [Fact]
    public async Task Agent_in_nawerktijd_is_niet_kiesbaar()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 30, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");

        await sut.BeginWrapUpAsync("agent1001");
        var snapshot = await sut.GetAsync("agent1001");

        Assert.Equal(AgentStatus.WrapUp, snapshot!.Status);
        Assert.Null(await sut.TryReserveForCallAsync(["support"]));
    }

    [Fact]
    public async Task Klaar_knop_maakt_agent_in_nawerktijd_weer_beschikbaar()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 30, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");
        await sut.BeginWrapUpAsync("agent1001");

        var snapshot = await sut.FinishWrapUpAsync("agent1001");

        Assert.Equal(AgentStatus.Available, snapshot!.Status);
        Assert.NotNull(await sut.TryReserveForCallAsync(["support"]));
    }

    [Fact]
    public async Task Nawerktijd_nul_maakt_agent_meteen_beschikbaar()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");

        await sut.BeginWrapUpAsync("agent1001");
        var snapshot = await sut.GetAsync("agent1001");

        Assert.Equal(AgentStatus.Available, snapshot!.Status);
    }

    [Fact]
    public async Task Uitgelogde_agent_wordt_niet_gereserveerd()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");
        await sut.LogoutAsync("agent1001");

        Assert.Null(await sut.TryReserveForCallAsync(["support"]));
    }

    [Fact]
    public async Task Agent_op_pauze_is_niet_kiesbaar_voor_automatische_toewijzing()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");

        await sut.SetPresenceAsync("agent1001", Presence.Break);

        Assert.Null(await sut.TryReserveForCallAsync(["support"]));
    }

    [Fact]
    public async Task Presence_terug_naar_beschikbaar_maakt_weer_kiesbaar()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");
        await sut.SetPresenceAsync("agent1001", Presence.Unavailable);
        await sut.SetPresenceAsync("agent1001", Presence.Available);

        Assert.NotNull(await sut.TryReserveForCallAsync(["support"]));
    }

    [Fact]
    public async Task Handmatig_reserveren_kan_ook_terwijl_op_pauze()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");
        await sut.SetPresenceAsync("agent1001", Presence.Break);

        Assert.NotNull(await sut.TryReserveSpecificAsync("agent1001"));
    }

    [Fact]
    public async Task Handmatig_reserveren_kan_niet_tijdens_een_lopend_gesprek()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync("agent1001");
        await sut.TryReserveSpecificAsync("agent1001"); // → Ringing
        await sut.ConfirmOnCallAsync("agent1001"); // → OnCall

        Assert.Null(await sut.TryReserveSpecificAsync("agent1001"));
    }
}
