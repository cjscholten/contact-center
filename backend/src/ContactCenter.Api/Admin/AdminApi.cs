using System.Text.RegularExpressions;
using ContactCenter.Api.CallFlow;
using ContactCenter.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.Admin;

/// <summary>
/// Beheer-API (/api/admin) voor ZetaBeheer: CRUD op de configuratie-entiteiten. Nog zonder
/// authenticatie (Keycloak volgt) — alleen dev-only achter de NSG. B1 dekt de wachtrijen.
/// De kernlogica (validatie + create/update) staat in losse, testbare methodes; de endpoints
/// zijn dunne wrappers die het resultaat naar een HTTP-status vertalen.
/// </summary>
public static partial class AdminApi
{
    private const int DefaultWrapUpSeconds = 30;

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex QueueNameRegex();

    [GeneratedRegex(@"^\+[0-9]{6,15}$")]
    private static partial Regex E164Regex();

    [GeneratedRegex(@"^\+?[0-9]{3,15}$")]
    private static partial Regex ForwardNumberRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9_.-]+$")]
    private static partial Regex AgentNameRegex();

    public static void MapAdminApi(this WebApplication app)
    {
        var queues = app.MapGroup("/api/admin/queues");

        queues.MapGet("", async (IDbContextFactory<CcDbContext> factory, QueueDecisionService decide, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var all = await db.Queues.AsNoTracking()
                .Include(q => q.Numbers)
                .Include(q => q.OpeningHours)
                .OrderBy(q => q.DisplayName)
                .ToListAsync(ct);
            return Results.Ok(all.Select(q => new QueueListItem(
                q.Id, q.Name, q.DisplayName, q.Numbers.Count, q.AdHocClosed, IsOpenNow(decide, q, now))));
        });

        queues.MapGet("/{id:int}", async (int id, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var q = await db.Queues.AsNoTracking()
                .Include(x => x.Numbers).Include(x => x.OpeningHours)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return q is null ? Results.NotFound() : Results.Ok(ToDetail(q));
        });

        queues.MapPost("", async (QueueWriteRequest req, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var result = await CreateQueueAsync(db, req, ct);
            return result.Error is { } error
                ? Results.BadRequest(new { error })
                : Results.Created($"/api/admin/queues/{result.Detail!.Id}", result.Detail);
        });

        queues.MapPut("/{id:int}", async (int id, QueueWriteRequest req, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var result = await UpdateQueueAsync(db, id, req, ct);
            if (result is null) return Results.NotFound();
            return result.Error is { } error ? Results.BadRequest(new { error }) : Results.Ok(result.Detail);
        });

        queues.MapDelete("/{id:int}", async (int id, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var q = await db.Queues.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (q is null) return Results.NotFound();
            db.Queues.Remove(q); // cascade verwijdert openingstijden + nummers
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var agents = app.MapGroup("/api/admin/agents");

        agents.MapGet("", async (IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var all = await db.Agents.AsNoTracking()
                .Include(a => a.QueueAssignments).ThenInclude(qa => qa.Queue)
                .OrderBy(a => a.DisplayName)
                .ToListAsync(ct);
            return Results.Ok(all.Select(a => new AgentListItem(
                a.Id, a.Name, a.DisplayName, a.Endpoint,
                [.. a.QueueAssignments.Select(qa => qa.Queue!.DisplayName).OrderBy(n => n)])));
        });

        agents.MapGet("/{id:int}", async (int id, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var a = await db.Agents.AsNoTracking()
                .Include(x => x.QueueAssignments)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return a is null ? Results.NotFound() : Results.Ok(ToAgentDetail(a));
        });

        agents.MapPost("", async (AgentWriteRequest req, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var result = await CreateAgentAsync(db, req, ct);
            return result.Error is { } error
                ? Results.BadRequest(new { error })
                : Results.Created($"/api/admin/agents/{result.Detail!.Id}", result.Detail);
        });

        agents.MapPut("/{id:int}", async (int id, AgentWriteRequest req, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var result = await UpdateAgentAsync(db, id, req, ct);
            if (result is null) return Results.NotFound();
            return result.Error is { } error ? Results.BadRequest(new { error }) : Results.Ok(result.Detail);
        });

        agents.MapDelete("/{id:int}", async (int id, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var a = await db.Agents.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            db.Agents.Remove(a); // cascade verwijdert de wachtrij-toewijzingen
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var contacts = app.MapGroup("/api/admin/contacts");

        contacts.MapGet("", async (IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var all = await db.Contacts.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
            return Results.Ok(all.Select(ToContactDto));
        });

        contacts.MapGet("/{id:int}", async (int id, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var c = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return c is null ? Results.NotFound() : Results.Ok(ToContactDto(c));
        });

        contacts.MapPost("", async (ContactWriteRequest req, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var result = await CreateContactAsync(db, req, ct);
            return result.Error is { } error
                ? Results.BadRequest(new { error })
                : Results.Created($"/api/admin/contacts/{result.Detail!.Id}", result.Detail);
        });

        contacts.MapPut("/{id:int}", async (int id, ContactWriteRequest req, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var result = await UpdateContactAsync(db, id, req, ct);
            if (result is null) return Results.NotFound();
            return result.Error is { } error ? Results.BadRequest(new { error }) : Results.Ok(result.Detail);
        });

        contacts.MapDelete("/{id:int}", async (int id, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            db.Contacts.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var settings = app.MapGroup("/api/admin/settings");

        settings.MapGet("", async (IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var s = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
            return Results.Ok(new SettingsDto(s?.WrapUpSeconds ?? DefaultWrapUpSeconds));
        });

        settings.MapPut("", async (SettingsWriteRequest req, IDbContextFactory<CcDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var (dto, error) = await UpdateSettingsAsync(db, req, ct);
            return error is { } e ? Results.BadRequest(new { error = e }) : Results.Ok(dto);
        });
    }

    // --- Testbare kernlogica ---------------------------------------------------

    public static async Task<QueueResult> CreateQueueAsync(CcDbContext db, QueueWriteRequest req, CancellationToken ct = default)
    {
        var name = req.Name.Trim();
        if (!QueueNameRegex().IsMatch(name))
            return QueueResult.Fail("Naam mag alleen kleine letters en cijfers bevatten (bv. 'sales').");
        if (await db.Queues.AnyAsync(q => q.Name == name, ct))
            return QueueResult.Fail($"Er bestaat al een wachtrij met de naam '{name}'.");
        if (await ValidateAsync(db, req, queueId: null, ct) is { } error)
            return QueueResult.Fail(error);

        var q = new QueueConfig { Name = name, DisplayName = req.DisplayName.Trim() };
        ApplyScalars(q, req);
        ReplaceChildren(q, req);
        db.Queues.Add(q);
        await db.SaveChangesAsync(ct);
        return QueueResult.Ok(ToDetail(q));
    }

    /// <summary>Werkt een wachtrij bij. Geeft null bij onbekende id; Name is read-only bij wijzigen.</summary>
    public static async Task<QueueResult?> UpdateQueueAsync(CcDbContext db, int id, QueueWriteRequest req, CancellationToken ct = default)
    {
        var q = await db.Queues
            .Include(x => x.Numbers).Include(x => x.OpeningHours)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q is null) return null;
        if (await ValidateAsync(db, req, queueId: id, ct) is { } error)
            return QueueResult.Fail(error);

        q.DisplayName = req.DisplayName.Trim();
        ApplyScalars(q, req);
        ReplaceChildren(q, req);
        await db.SaveChangesAsync(ct);
        return QueueResult.Ok(ToDetail(q));
    }

    // --- Agents ----------------------------------------------------------------

    public static async Task<AgentResult> CreateAgentAsync(CcDbContext db, AgentWriteRequest req, CancellationToken ct = default)
    {
        var name = req.Name.Trim();
        if (!AgentNameRegex().IsMatch(name))
            return AgentResult.Fail("Naam mag alleen letters, cijfers, '.', '_' en '-' bevatten.");
        if (await db.Agents.AnyAsync(a => a.Name == name, ct))
            return AgentResult.Fail($"Er bestaat al een agent met de naam '{name}'.");
        if (await ValidateAgentAsync(db, req, ct) is { } error)
            return AgentResult.Fail(error);

        var agent = new Agent { Name = name, DisplayName = req.DisplayName.Trim(), Endpoint = req.Endpoint.Trim() };
        ApplyAgentQueues(agent, req.QueueIds);
        db.Agents.Add(agent);
        await db.SaveChangesAsync(ct);
        return AgentResult.Ok(ToAgentDetail(agent));
    }

    /// <summary>Werkt een agent bij. Geeft null bij onbekende id; Name is read-only bij wijzigen.</summary>
    public static async Task<AgentResult?> UpdateAgentAsync(CcDbContext db, int id, AgentWriteRequest req, CancellationToken ct = default)
    {
        var agent = await db.Agents.Include(a => a.QueueAssignments).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null) return null;
        if (await ValidateAgentAsync(db, req, ct) is { } error)
            return AgentResult.Fail(error);

        agent.DisplayName = req.DisplayName.Trim();
        agent.Endpoint = req.Endpoint.Trim();
        ApplyAgentQueues(agent, req.QueueIds);
        await db.SaveChangesAsync(ct);
        return AgentResult.Ok(ToAgentDetail(agent));
    }

    private static async Task<string?> ValidateAgentAsync(CcDbContext db, AgentWriteRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return "Weergavenaam is verplicht.";
        if (string.IsNullOrWhiteSpace(req.Endpoint))
            return "Endpoint is verplicht (bv. PJSIP/agent1001).";

        var ids = req.QueueIds.Distinct().ToList();
        if (ids.Count > 0)
        {
            var existing = await db.Queues.CountAsync(q => ids.Contains(q.Id), ct);
            if (existing != ids.Count)
                return "Eén of meer geselecteerde wachtrijen bestaan niet.";
        }
        return null;
    }

    private static void ApplyAgentQueues(Agent agent, IReadOnlyList<int> queueIds)
    {
        agent.QueueAssignments.Clear();
        foreach (var id in queueIds.Distinct())
            agent.QueueAssignments.Add(new AgentQueueAssignment { QueueConfigId = id });
    }

    private static AgentDetail ToAgentDetail(Agent a) => new(
        a.Id, a.Name, a.DisplayName, a.Endpoint,
        [.. a.QueueAssignments.Select(qa => qa.QueueConfigId).OrderBy(x => x)]);

    // --- Contacten -------------------------------------------------------------

    public static async Task<ContactResult> CreateContactAsync(CcDbContext db, ContactWriteRequest req, CancellationToken ct = default)
    {
        if (ValidateContact(req) is { } error)
            return ContactResult.Fail(error);
        var c = new Contact
        {
            Name = req.Name.Trim(),
            Number = req.Number.Trim(),
            Department = string.IsNullOrWhiteSpace(req.Department) ? null : req.Department.Trim(),
        };
        db.Contacts.Add(c);
        await db.SaveChangesAsync(ct);
        return ContactResult.Ok(ToContactDto(c));
    }

    /// <summary>Werkt een contact bij. Geeft null bij onbekende id.</summary>
    public static async Task<ContactResult?> UpdateContactAsync(CcDbContext db, int id, ContactWriteRequest req, CancellationToken ct = default)
    {
        var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        if (ValidateContact(req) is { } error)
            return ContactResult.Fail(error);
        c.Name = req.Name.Trim();
        c.Number = req.Number.Trim();
        c.Department = string.IsNullOrWhiteSpace(req.Department) ? null : req.Department.Trim();
        await db.SaveChangesAsync(ct);
        return ContactResult.Ok(ToContactDto(c));
    }

    private static string? ValidateContact(ContactWriteRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return "Naam is verplicht.";
        if (!E164Regex().IsMatch(req.Number.Trim()))
            return $"Ongeldig nummer '{req.Number}' (verwacht E.164, bv. +319...).";
        return null;
    }

    private static ContactDto ToContactDto(Contact c) => new(c.Id, c.Name, c.Number, c.Department);

    // --- Instellingen ----------------------------------------------------------

    public static async Task<(SettingsDto? Dto, string? Error)> UpdateSettingsAsync(
        CcDbContext db, SettingsWriteRequest req, CancellationToken ct = default)
    {
        if (req.WrapUpSeconds is < 0 or > 3600)
            return (null, "Nawerktijd moet tussen 0 en 3600 seconden liggen.");
        var s = await db.Settings.FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new GlobalSettings();
            db.Settings.Add(s);
        }
        s.WrapUpSeconds = req.WrapUpSeconds;
        await db.SaveChangesAsync(ct);
        return (new SettingsDto(s.WrapUpSeconds), null);
    }

    private static bool IsOpenNow(QueueDecisionService decide, QueueConfig q, DateTimeOffset now)
    {
        try { return decide.IsOpen(q, now); }
        catch { return false; } // bv. onbekende tijdzone — toon dan 'dicht'
    }

    private static async Task<string?> ValidateAsync(CcDbContext db, QueueWriteRequest req, int? queueId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return "Weergavenaam is verplicht.";

        try { _ = TimeZoneInfo.FindSystemTimeZoneById(req.TimeZone); }
        catch { return $"Onbekende tijdzone '{req.TimeZone}'."; }

        foreach (var w in req.OpeningHours)
            if (w.Opens >= w.Closes)
                return $"Openingsvenster op {w.Day} moet een eindtijd ná de begintijd hebben.";

        var numbers = req.Numbers.Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        foreach (var n in numbers)
            if (!E164Regex().IsMatch(n))
                return $"Ongeldig nummer '{n}' (verwacht E.164, bv. +319...).";
        if (numbers.Distinct().Count() != numbers.Count)
            return "De nummerlijst bevat dubbele nummers.";
        var clash = await db.InboundNumbers
            .Where(x => x.QueueConfigId != (queueId ?? 0) && numbers.Contains(x.Number))
            .Select(x => x.Number).ToListAsync(ct);
        if (clash.Count > 0)
            return $"Nummer(s) al in gebruik door een andere wachtrij: {string.Join(", ", clash)}.";

        if (!string.IsNullOrWhiteSpace(req.AdHocForwardNumber) && !ForwardNumberRegex().IsMatch(req.AdHocForwardNumber.Trim()))
            return "Ongeldig doorschakelnummer.";

        return null;
    }

    private static void ApplyScalars(QueueConfig q, QueueWriteRequest req)
    {
        q.WelcomePrompt = req.WelcomePrompt.Trim();
        q.ClosedPrompt = req.ClosedPrompt.Trim();
        q.TimeZone = req.TimeZone;
        q.AdHocClosed = req.AdHocClosed;
        q.AdHocForwardNumber = string.IsNullOrWhiteSpace(req.AdHocForwardNumber) ? null : req.AdHocForwardNumber.Trim();
    }

    private static void ReplaceChildren(QueueConfig q, QueueWriteRequest req)
    {
        q.OpeningHours.Clear();
        foreach (var w in req.OpeningHours)
            q.OpeningHours.Add(new OpeningHoursWindow { Day = w.Day, Opens = w.Opens, Closes = w.Closes });

        q.Numbers.Clear();
        foreach (var n in req.Numbers.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct())
            q.Numbers.Add(new InboundNumber { Number = n });
    }

    private static QueueDetail ToDetail(QueueConfig q) => new(
        q.Id, q.Name, q.DisplayName, q.WelcomePrompt, q.ClosedPrompt,
        q.AdHocClosed, q.AdHocForwardNumber, q.TimeZone,
        [.. q.OpeningHours.OrderBy(w => w.Day).ThenBy(w => w.Opens).Select(w => new OpeningHoursDto(w.Day, w.Opens, w.Closes))],
        [.. q.Numbers.OrderBy(n => n.Number).Select(n => n.Number)]);
}

public sealed record QueueResult(QueueDetail? Detail, string? Error)
{
    public static QueueResult Ok(QueueDetail detail) => new(detail, null);
    public static QueueResult Fail(string error) => new(null, error);
}

public sealed record QueueListItem(
    int Id, string Name, string DisplayName, int NumberCount, bool AdHocClosed, bool OpenNow);

public sealed record OpeningHoursDto(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);

public sealed record QueueDetail(
    int Id, string Name, string DisplayName, string WelcomePrompt, string ClosedPrompt,
    bool AdHocClosed, string? AdHocForwardNumber, string TimeZone,
    IReadOnlyList<OpeningHoursDto> OpeningHours, IReadOnlyList<string> Numbers);

public sealed record QueueWriteRequest(
    string Name, string DisplayName, string WelcomePrompt, string ClosedPrompt,
    bool AdHocClosed, string? AdHocForwardNumber, string TimeZone,
    IReadOnlyList<OpeningHoursDto> OpeningHours, IReadOnlyList<string> Numbers);

public sealed record AgentResult(AgentDetail? Detail, string? Error)
{
    public static AgentResult Ok(AgentDetail detail) => new(detail, null);
    public static AgentResult Fail(string error) => new(null, error);
}

public sealed record AgentListItem(
    int Id, string Name, string DisplayName, string Endpoint, IReadOnlyList<string> Queues);

public sealed record AgentDetail(
    int Id, string Name, string DisplayName, string Endpoint, IReadOnlyList<int> QueueIds);

public sealed record AgentWriteRequest(
    string Name, string DisplayName, string Endpoint, IReadOnlyList<int> QueueIds);

public sealed record ContactResult(ContactDto? Detail, string? Error)
{
    public static ContactResult Ok(ContactDto detail) => new(detail, null);
    public static ContactResult Fail(string error) => new(null, error);
}

public sealed record ContactDto(int Id, string Name, string Number, string? Department);

public sealed record ContactWriteRequest(string Name, string Number, string? Department);

public sealed record SettingsDto(int WrapUpSeconds);

public sealed record SettingsWriteRequest(int WrapUpSeconds);
