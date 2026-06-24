using ContactCenter.Api.Data;
using ContactCenter.Api.Directory;

namespace ContactCenter.Tests;

public class DirectoryServiceTests
{
    private static TestDbContextFactory SeededFactory()
    {
        var factory = new TestDbContextFactory();
        factory.Seed(wrapUpSeconds: 0, ("agent1001", ["support"]), ("agent1002", ["support"]));
        using var db = factory.CreateDbContext();
        db.Contacts.Add(new Contact { Name = "Boekhouding", Number = "+31201234520", Department = "Finance" });
        db.SaveChanges();
        return factory;
    }

    [Fact]
    public async Task Lege_zoekterm_geeft_agents_en_contacten()
    {
        var sut = new DirectoryService(SeededFactory());

        var all = await sut.SearchAsync("");

        Assert.Contains(all, e => e.Kind == "agent" && e.Target == "agent1001");
        Assert.Contains(all, e => e.Kind == "contact" && e.Label == "Boekhouding" && e.Target == "+31201234520");
    }

    [Fact]
    public async Task Filtert_hoofdletterongevoelig_op_zoekterm()
    {
        var sut = new DirectoryService(SeededFactory());

        var result = await sut.SearchAsync("BOEK");

        var entry = Assert.Single(result);
        Assert.Equal("contact", entry.Kind);
        Assert.Equal("Boekhouding", entry.Label);
    }

    [Fact]
    public async Task Sluit_de_zoekende_agent_zelf_uit()
    {
        var sut = new DirectoryService(SeededFactory());

        var result = await sut.SearchAsync("", excludeAgent: "agent1001");

        Assert.DoesNotContain(result, e => e.Target == "agent1001");
        Assert.Contains(result, e => e.Target == "agent1002");
    }
}
