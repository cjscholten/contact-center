using ContactCenter.Api.Agents;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContactCenter.Tests;

public class AgentStateServiceTests
{
    private const int T = 0; // testtenant (gelijk aan de default-TenantId van geseede data)

    private static string Q(string queue) => AgentStateService.QueueKey(T, queue);

    private static AgentStateService Build(TestDbContextFactory factory)
        => new(factory, NullLogger<AgentStateService>.Instance);

    [Fact]
    public async Task Reserveert_een_beschikbare_agent_in_de_wachtrij()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync(T, "agent1001");

        var reserved = await sut.TryReserveForCallAsync([Q("support")]);

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
        await sut.LoginAsync(T, "agent1001");
        await sut.LoginAsync(T, "agent1002");

        var first = await sut.TryReserveForCallAsync([Q("support")]);
        var second = await sut.TryReserveForCallAsync([Q("support")]);
        var third = await sut.TryReserveForCallAsync([Q("support")]);

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
        await sut.LoginAsync(T, "agent1001");

        Assert.Null(await sut.TryReserveForCallAsync([Q("sales")]));
    }

    [Fact]
    public async Task Agent_in_nawerktijd_is_niet_kiesbaar()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 30, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync(T, "agent1001");

        await sut.BeginWrapUpAsync(T, "agent1001");
        var snapshot = await sut.GetAsync(T, "agent1001");

        Assert.Equal(AgentStatus.WrapUp, snapshot!.Status);
        Assert.Null(await sut.TryReserveForCallAsync([Q("support")]));
    }

    [Fact]
    public async Task Klaar_knop_maakt_agent_in_nawerktijd_weer_beschikbaar()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 30, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync(T, "agent1001");
        await sut.BeginWrapUpAsync(T, "agent1001");

        var snapshot = await sut.FinishWrapUpAsync(T, "agent1001");

        Assert.Equal(AgentStatus.Available, snapshot!.Status);
        Assert.NotNull(await sut.TryReserveForCallAsync([Q("support")]));
    }

    [Fact]
    public async Task Nawerktijd_nul_maakt_agent_meteen_beschikbaar()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync(T, "agent1001");

        await sut.BeginWrapUpAsync(T, "agent1001");
        var snapshot = await sut.GetAsync(T, "agent1001");

        Assert.Equal(AgentStatus.Available, snapshot!.Status);
    }

    [Fact]
    public async Task Uitgelogde_agent_wordt_niet_gereserveerd()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync(T, "agent1001");
        await sut.LogoutAsync(T, "agent1001");

        Assert.Null(await sut.TryReserveForCallAsync([Q("support")]));
    }

    [Fact]
    public async Task Agent_op_pauze_is_niet_kiesbaar_voor_automatische_toewijzing()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync(T, "agent1001");

        await sut.SetPresenceAsync(T, "agent1001", Presence.Break);

        Assert.Null(await sut.TryReserveForCallAsync([Q("support")]));
    }

    [Fact]
    public async Task Presence_terug_naar_beschikbaar_maakt_weer_kiesbaar()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync(T, "agent1001");
        await sut.SetPresenceAsync(T, "agent1001", Presence.Unavailable);
        await sut.SetPresenceAsync(T, "agent1001", Presence.Available);

        Assert.NotNull(await sut.TryReserveForCallAsync([Q("support")]));
    }

    [Fact]
    public async Task Handmatig_reserveren_kan_ook_terwijl_op_pauze()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync(T, "agent1001");
        await sut.SetPresenceAsync(T, "agent1001", Presence.Break);

        Assert.NotNull(await sut.TryReserveSpecificAsync(T, "agent1001"));
    }

    [Fact]
    public async Task Handmatig_reserveren_kan_niet_tijdens_een_lopend_gesprek()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]));
        var sut = Build(factory);
        await sut.LoginAsync(T, "agent1001");
        await sut.TryReserveSpecificAsync(T, "agent1001"); // → Ringing
        await sut.ConfirmOnCallAsync(T, "agent1001"); // → OnCall

        Assert.Null(await sut.TryReserveSpecificAsync(T, "agent1001"));
    }
}
