using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.Directory;

/// <summary>Een doorverbind-bestemming: een collega-agent of een contact.</summary>
public sealed record DirectoryEntry(string Kind, string Label, string Detail, string Target);

/// <summary>Doorzoekt collega-agents én de contactenlijst voor het doorverbind-paneel.</summary>
public sealed class DirectoryService(IDbContextFactory<CcDbContext> dbFactory)
{
    public async Task<IReadOnlyList<DirectoryEntry>> SearchAsync(
        string? query, string? excludeAgent = null, CancellationToken ct = default)
    {
        var q = (query ?? string.Empty).Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var agents = await db.Agents.AsNoTracking()
            .Where(a => excludeAgent == null || a.Name != excludeAgent)
            .Where(a => q == "" || a.DisplayName.ToLower().Contains(q) || a.Name.ToLower().Contains(q))
            .OrderBy(a => a.DisplayName)
            .Select(a => new DirectoryEntry("agent", a.DisplayName, "Collega", a.Name))
            .ToListAsync(ct);

        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => q == "" || c.Name.ToLower().Contains(q))
            .OrderBy(c => c.Name)
            .Select(c => new DirectoryEntry("contact", c.Name, c.Department ?? "", c.Number))
            .ToListAsync(ct);

        return [.. agents, .. contacts];
    }
}
