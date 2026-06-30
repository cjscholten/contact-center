using ContactCenter.Api.Admin;
using ContactCenter.Api.Auth;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Tests;

public class TenantIsolationTests
{
    private static QueueWriteRequest QueueReq(string name, string display, string number) =>
        new(name, display, "", "", "nl_NL-pim-medium", false, null, "Europe/Amsterdam",
            [new OpeningHoursDto(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))],
            [number], "default");

    [Fact]
    public async Task Reads_van_de_ene_tenant_zien_geen_data_van_de_andere()
    {
        var factory = new TestDbContextFactory();

        // tenant 1 en tenant 2 krijgen elk een eigen wachtrij 'support'
        factory.Tenant.TenantId = 1;
        await using (var db = factory.CreateDbContext())
            await AdminApi.CreateQueueAsync(db, QueueReq("support", "Tenant 1 Support", "+3110000001"));

        factory.Tenant.TenantId = 2;
        await using (var db = factory.CreateDbContext())
            await AdminApi.CreateQueueAsync(db, QueueReq("support", "Tenant 2 Support", "+3110000002"));

        // tenant 1 ziet alleen de eigen wachtrij
        factory.Tenant.TenantId = 1;
        await using (var db = factory.CreateDbContext())
        {
            var queues = await db.Queues.ToListAsync();
            var q = Assert.Single(queues);
            Assert.Equal("Tenant 1 Support", q.DisplayName);
            Assert.Equal(1, q.TenantId);
        }

        // tenant 2 ziet alleen de eigene
        factory.Tenant.TenantId = 2;
        await using (var db = factory.CreateDbContext())
        {
            var q = Assert.Single(await db.Queues.ToListAsync());
            Assert.Equal("Tenant 2 Support", q.DisplayName);
        }
    }

    [Fact]
    public async Task Gelijknamige_wachtrij_mag_in_twee_tenants_bestaan()
    {
        var factory = new TestDbContextFactory();

        factory.Tenant.TenantId = 1;
        await using (var db = factory.CreateDbContext())
        {
            var r = await AdminApi.CreateQueueAsync(db, QueueReq("support", "S1", "+3110000001"));
            Assert.Null(r.Error);
        }

        factory.Tenant.TenantId = 2;
        await using (var db = factory.CreateDbContext())
        {
            var r = await AdminApi.CreateQueueAsync(db, QueueReq("support", "S2", "+3110000002"));
            Assert.Null(r.Error); // geen naamconflict tussen tenants
        }
    }

    [Fact]
    public async Task Hetzelfde_inkomende_nummer_blijft_globaal_uniek_over_tenants_heen()
    {
        var factory = new TestDbContextFactory();

        factory.Tenant.TenantId = 1;
        await using (var db = factory.CreateDbContext())
            await AdminApi.CreateQueueAsync(db, QueueReq("support", "S1", "+3110009999"));

        factory.Tenant.TenantId = 2;
        await using (var db = factory.CreateDbContext())
        {
            var r = await AdminApi.CreateQueueAsync(db, QueueReq("sales", "S2", "+3110009999")); // zelfde DID
            Assert.NotNull(r.Error); // DID hoort bij precies één tenant
        }
    }

    [Fact]
    public async Task Create_zet_de_tenant_van_de_context()
    {
        var factory = new TestDbContextFactory { };
        factory.Tenant.TenantId = 7;
        await using var db = factory.CreateDbContext();

        await AdminApi.CreateContactAsync(db, new ContactWriteRequest("Receptie", "+31201234500", null));

        await using var verify = factory.CreateDbContext();
        var c = await verify.Contacts.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(7, c.TenantId);
    }

    [Theory]
    [InlineData("http://20.107.0.204:8080/realms/contactcenter", "contactcenter")]
    [InlineData("https://id.example.com/realms/tenant-acme", "tenant-acme")]
    [InlineData("http://localhost:8080/realms/contactcenter/", "contactcenter")]
    [InlineData("http://localhost:8080/auth", null)]
    [InlineData(null, null)]
    public void Realm_wordt_uit_de_issuer_gehaald(string? issuer, string? expected)
        => Assert.Equal(expected, KeycloakRealmKeys.RealmFromIssuer(issuer));
}
