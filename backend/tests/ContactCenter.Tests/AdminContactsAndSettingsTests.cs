using ContactCenter.Api.Admin;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Tests;

public class AdminContactsAndSettingsTests
{
    // --- Contacten ---

    [Fact]
    public async Task Create_geldig_contact_slaat_op()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();

        var result = await AdminApi.CreateContactAsync(db, new ContactWriteRequest("Receptie", "+31201234500", "Kantoor"));

        Assert.Null(result.Error);
        Assert.Equal("Receptie", result.Detail!.Name);

        await using var verify = factory.CreateDbContext();
        var saved = await verify.Contacts.SingleAsync();
        Assert.Equal("+31201234500", saved.Number);
        Assert.Equal("Kantoor", saved.Department);
    }

    [Fact]
    public async Task Create_zonder_naam_faalt()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var result = await AdminApi.CreateContactAsync(db, new ContactWriteRequest("   ", "+31201234500", null));
        Assert.NotNull(result.Error);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task Create_met_ongeldig_nummer_faalt()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var result = await AdminApi.CreateContactAsync(db, new ContactWriteRequest("Receptie", "06-12345678", null));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Update_wijzigt_velden()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var created = await AdminApi.CreateContactAsync(db, new ContactWriteRequest("Receptie", "+31201234500", "Kantoor"));
        var id = created.Detail!.Id;

        var result = await AdminApi.UpdateContactAsync(db, id, new ContactWriteRequest("Helpdesk", "+31201234510", null));

        Assert.NotNull(result);
        Assert.Null(result!.Error);
        Assert.Equal("Helpdesk", result.Detail!.Name);
        Assert.Equal("+31201234510", result.Detail.Number);
        Assert.Null(result.Detail.Department);
    }

    [Fact]
    public async Task Update_van_onbekend_contact_geeft_null()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        Assert.Null(await AdminApi.UpdateContactAsync(db, 999, new ContactWriteRequest("X", "+31201234500", null)));
    }

    // --- Instellingen ---

    [Fact]
    public async Task Settings_bijwerken_slaat_nawerktijd_op()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();

        var (dto, error) = await AdminApi.UpdateSettingsAsync(db, new SettingsWriteRequest(45));

        Assert.Null(error);
        Assert.Equal(45, dto!.WrapUpSeconds);

        await using var verify = factory.CreateDbContext();
        Assert.Equal(45, (await verify.Settings.SingleAsync()).WrapUpSeconds);
    }

    [Fact]
    public async Task Settings_negatieve_nawerktijd_faalt()
    {
        var factory = new TestDbContextFactory();
        await using var db = factory.CreateDbContext();
        var (dto, error) = await AdminApi.UpdateSettingsAsync(db, new SettingsWriteRequest(-5));
        Assert.NotNull(error);
        Assert.Null(dto);
    }

    [Fact]
    public async Task Settings_bijwerken_werkt_bestaande_rij_bij_zonder_duplicaat()
    {
        var factory = new TestDbContextFactory();
        await using (var seed = factory.CreateDbContext())
        {
            seed.Settings.Add(new GlobalSettings { WrapUpSeconds = 30 });
            await seed.SaveChangesAsync();
        }
        await using var db = factory.CreateDbContext();

        await AdminApi.UpdateSettingsAsync(db, new SettingsWriteRequest(60));

        await using var verify = factory.CreateDbContext();
        var all = await verify.Settings.ToListAsync();
        Assert.Single(all); // geen tweede rij aangemaakt
        Assert.Equal(60, all[0].WrapUpSeconds);
    }
}
