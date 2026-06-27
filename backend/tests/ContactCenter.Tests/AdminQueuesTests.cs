using ContactCenter.Api.Admin;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Tests;

public class AdminQueuesTests
{
    private static QueueWriteRequest Req(
        string name = "sales",
        string display = "Sales",
        IReadOnlyList<OpeningHoursDto>? hours = null,
        IReadOnlyList<string>? numbers = null,
        bool adHocClosed = false,
        string? forward = null,
        string moh = "default")
        => new(name, display, "sound:welcome", "sound:closed", adHocClosed, forward, "Europe/Amsterdam",
            hours ?? [new OpeningHoursDto(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))],
            numbers ?? ["+31201234599"], moh);

    [Fact]
    public async Task Create_geldige_wachtrij_slaat_nummers_en_openingstijden_op()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();

        var result = await AdminApi.CreateQueueAsync(db, Req(
            numbers: ["+31201230001"],
            hours: [new(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0)),
                    new(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(12, 0))]));

        Assert.Null(result.Error);
        Assert.Equal("sales", result.Detail!.Name);
        Assert.Equal(2, result.Detail.OpeningHours.Count);
        Assert.Equal(["+31201230001"], result.Detail.Numbers);

        await using var verify = factory.CreateDbContext();
        var saved = await verify.Queues.Include(q => q.Numbers).Include(q => q.OpeningHours).SingleAsync();
        Assert.Single(saved.Numbers);
        Assert.Equal(2, saved.OpeningHours.Count);
    }

    [Fact]
    public async Task Create_met_ongeldige_naam_faalt()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var result = await AdminApi.CreateQueueAsync(db, Req(name: "Sales 1")); // hoofdletters/spatie
        Assert.NotNull(result.Error);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task Create_met_dubbele_naam_faalt()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        await AdminApi.CreateQueueAsync(db, Req(name: "sales", numbers: ["+31201230001"]));
        var result = await AdminApi.CreateQueueAsync(db, Req(name: "sales", numbers: ["+31201230002"]));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Create_met_ongeldig_nummer_faalt()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var result = await AdminApi.CreateQueueAsync(db, Req(numbers: ["06-12345678"]));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Create_met_nummer_van_andere_wachtrij_faalt()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        await AdminApi.CreateQueueAsync(db, Req(name: "sales", numbers: ["+31201230001"]));
        var result = await AdminApi.CreateQueueAsync(db, Req(name: "support", numbers: ["+31201230001"]));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Create_met_omgekeerd_openingsvenster_faalt()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var result = await AdminApi.CreateQueueAsync(db, Req(
            hours: [new(DayOfWeek.Monday, new TimeOnly(17, 0), new TimeOnly(9, 0))]));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Update_vervangt_nummers_en_openingstijden()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var created = await AdminApi.CreateQueueAsync(db, Req(name: "sales",
            numbers: ["+31201230001"], hours: [new(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))]));
        var id = created.Detail!.Id;

        var result = await AdminApi.UpdateQueueAsync(db, id, Req(name: "sales", display: "Verkoop",
            numbers: ["+31201230002", "+31201230003"],
            hours: [new(DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(20, 0))]));

        Assert.NotNull(result);
        Assert.Null(result!.Error);
        Assert.Equal("Verkoop", result.Detail!.DisplayName);
        Assert.Equal(["+31201230002", "+31201230003"], result.Detail.Numbers);
        Assert.Single(result.Detail.OpeningHours);

        await using var verify = factory.CreateDbContext();
        var saved = await verify.Queues.Include(q => q.Numbers).Include(q => q.OpeningHours).SingleAsync(q => q.Id == id);
        Assert.Equal(2, saved.Numbers.Count); // oude +...0001 verwijderd
        Assert.Single(saved.OpeningHours);
        Assert.Equal(DayOfWeek.Friday, saved.OpeningHours[0].Day);
    }

    [Fact]
    public async Task Update_van_onbekende_wachtrij_geeft_null()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        Assert.Null(await AdminApi.UpdateQueueAsync(db, 999, Req()));
    }

    [Fact]
    public async Task Update_laat_de_technische_naam_ongemoeid()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var created = await AdminApi.CreateQueueAsync(db, Req(name: "sales"));
        var id = created.Detail!.Id;

        var result = await AdminApi.UpdateQueueAsync(db, id, Req(name: "ietsanders", display: "Sales 2"));

        Assert.NotNull(result);
        Assert.Null(result!.Error);
        Assert.Equal("sales", result.Detail!.Name); // naam is read-only bij wijzigen
        Assert.Equal("Sales 2", result.Detail.DisplayName);
    }
}
