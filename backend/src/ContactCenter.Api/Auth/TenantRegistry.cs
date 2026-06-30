using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.Auth;

/// <summary>
/// In-memory index van tenants op realmnaam, geladen uit de <c>Tenants</c>-tabel. Gebruikt door
/// de JWT-validatie (issuer → realm → tenant) en de tenant-middleware. Herlaadbaar zodat een
/// nieuw toegevoegde tenant zonder herstart actief wordt.
/// </summary>
public interface ITenantRegistry
{
    /// <summary>Zoekt een ingeschakelde tenant op realmnaam. Geeft <c>false</c> als die niet bestaat of uit staat.</summary>
    bool TryGetByRealm(string realm, out TenantInfo tenant);

    /// <summary>(Her)laadt de tenants uit de database.</summary>
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed record TenantInfo(int Id, string Slug, string Realm);

public sealed class TenantRegistry(IDbContextFactory<CcDbContext> dbFactory, ILogger<TenantRegistry> logger)
    : ITenantRegistry
{
    private volatile IReadOnlyDictionary<string, TenantInfo> _byRealm =
        new Dictionary<string, TenantInfo>(StringComparer.OrdinalIgnoreCase);

    public bool TryGetByRealm(string realm, out TenantInfo tenant)
        => _byRealm.TryGetValue(realm, out tenant!);

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.Enabled).ToListAsync(ct);
        _byRealm = tenants.ToDictionary(
            t => t.Realm,
            t => new TenantInfo(t.Id, t.Slug, t.Realm),
            StringComparer.OrdinalIgnoreCase);
        logger.LogInformation("Tenant-registry geladen: {Count} actieve tenant(s) [{Realms}]",
            _byRealm.Count, string.Join(", ", _byRealm.Keys));
    }
}
