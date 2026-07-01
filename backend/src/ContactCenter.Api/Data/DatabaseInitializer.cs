using ContactCenter.Api.Tts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ContactCenter.Api.Data;

public static class DatabaseInitializer
{
    private const int MaxAttempts = 6;

    /// <summary>Migreert en seedt de database; niet-fataal zodat telefonie ook zonder database doorgaat.</summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var dbFactory = services.GetRequiredService<IDbContextFactory<CcDbContext>>();
        var tts = services.GetRequiredService<ITtsService>();
        // SIP-wachtwoord voor geseede agents uit config (env Agents__DefaultSipPassword); niet in git.
        var defaultSipPassword = services.GetRequiredService<IConfiguration>()["Agents:DefaultSipPassword"] ?? "";

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await db.Database.MigrateAsync();
                await SeedAsync(db, logger, defaultSipPassword);
                await RegeneratePromptsAsync(db, tts, logger);
                logger.LogInformation("Database gemigreerd en gereed");
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning("Database niet bereikbaar (poging {Attempt}/{Max}): {Reden} — opnieuw over 5s",
                    attempt, MaxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Database-initialisatie mislukt; backend draait door met terugvalgedrag (alles naar 'support')");
            }
        }
    }

    // De seed draait zonder tenant-context, dus de tenant-query-filter zou alles wegfilteren:
    // overal IgnoreQueryFilters() en TenantId expliciet zetten.
    private static async Task SeedAsync(CcDbContext db, ILogger logger, string sipPassword)
    {
        var defaultTenant = await EnsureTenantAsync(db, "default", "Standaard", "contactcenter", logger);
        var acmeTenant = await EnsureTenantAsync(db, "acme", "Acme BV", "tenant-acme", logger);

        await SeedDefaultTenantAsync(db, defaultTenant.Id, logger, sipPassword);
        await SeedAcmeTenantAsync(db, acmeTenant.Id, logger, sipPassword);
    }

    private static async Task<Tenant> EnsureTenantAsync(
        CcDbContext db, string slug, string displayName, string realm, ILogger logger)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null)
        {
            tenant = new Tenant { Slug = slug, DisplayName = displayName, Realm = realm };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            logger.LogInformation("Tenant '{Slug}' geseed (realm '{Realm}')", slug, realm);
        }
        return tenant;
    }

    private static async Task SeedDefaultTenantAsync(CcDbContext db, int tenantId, ILogger logger, string sipPassword)
    {
        if (!await db.Queues.IgnoreQueryFilters().AnyAsync(q => q.TenantId == tenantId))
        {
            var support = new QueueConfig
            {
                TenantId = tenantId,
                Name = "support",
                DisplayName = "Support",
                WelcomeText = "Welkom bij de klantenservice. Een moment geduld alstublieft, u wordt zo snel mogelijk geholpen.",
                Voice = "nl_NL-pim-medium",
                Numbers = [new InboundNumber { Number = "+19205008321" }],
                OpeningHours = AllDayOpeningHours(),
            };
            db.Queues.Add(support);
            await db.SaveChangesAsync();
            logger.LogInformation("Wachtrij 'support' geseed (tenant default) met nummer +19205008321 (24/7 open)");
        }

        if (!await db.Agents.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenantId))
        {
            var queueIds = await db.Queues.IgnoreQueryFilters()
                .Where(q => q.TenantId == tenantId).ToDictionaryAsync(q => q.Name, q => q.Id);
            db.Agents.AddRange(
                new Agent
                {
                    TenantId = tenantId,
                    Name = "agent1001",
                    DisplayName = "Agent 1001",
                    Endpoint = "PJSIP/agent1001",
                    SipPassword = sipPassword,
                    QueueAssignments = [.. queueIds.Values.Select(id => new AgentQueueAssignment { QueueConfigId = id })],
                },
                new Agent
                {
                    TenantId = tenantId,
                    Name = "agent1002",
                    DisplayName = "Agent 1002",
                    Endpoint = "PJSIP/agent1002",
                    SipPassword = sipPassword,
                    QueueAssignments = [new AgentQueueAssignment { QueueConfigId = queueIds["support"] }],
                });
            await db.SaveChangesAsync();
            logger.LogInformation("Agents 'agent1001' en 'agent1002' geseed (tenant default)");
        }

        await EnsureSettingsAsync(db, tenantId, logger);

        if (!await db.Contacts.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId))
        {
            db.Contacts.AddRange(
                new Contact { TenantId = tenantId, Name = "Receptie", Number = "+31201234500", Department = "Kantoor" },
                new Contact { TenantId = tenantId, Name = "Helpdesk tweede lijn", Number = "+31201234510", Department = "Support" },
                new Contact { TenantId = tenantId, Name = "Boekhouding", Number = "+31201234520", Department = "Finance" });
            await db.SaveChangesAsync();
            logger.LogInformation("Voorbeeldcontacten geseed (tenant default)");
        }
    }

    // Tweede voorbeeld-tenant zodat multi-tenant zichtbaar/testbaar is: eigen queue, eigen DID en
    // een agent met genamespacede SIP-endpoint (Asterisk-breed uniek).
    private static async Task SeedAcmeTenantAsync(CcDbContext db, int tenantId, ILogger logger, string sipPassword)
    {
        if (!await db.Queues.IgnoreQueryFilters().AnyAsync(q => q.TenantId == tenantId))
        {
            db.Queues.Add(new QueueConfig
            {
                TenantId = tenantId,
                Name = "support",
                DisplayName = "Acme Support",
                WelcomeText = "Welkom bij Acme. Een moment geduld alstublieft.",
                Voice = "nl_NL-pim-medium",
                Numbers = [new InboundNumber { Number = "+19205008322" }],
                OpeningHours = AllDayOpeningHours(),
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Wachtrij 'support' geseed (tenant acme) met nummer +19205008322");
        }

        if (!await db.Agents.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenantId))
        {
            var queueId = await db.Queues.IgnoreQueryFilters()
                .Where(q => q.TenantId == tenantId).Select(q => q.Id).FirstAsync();
            db.Agents.Add(new Agent
            {
                TenantId = tenantId,
                Name = "agent2001",
                DisplayName = "Acme Agent 2001",
                Endpoint = "PJSIP/acme-agent2001",
                SipPassword = sipPassword,
                QueueAssignments = [new AgentQueueAssignment { QueueConfigId = queueId }],
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Agent 'agent2001' geseed (tenant acme)");
        }

        await EnsureSettingsAsync(db, tenantId, logger);
    }

    private static async Task EnsureSettingsAsync(CcDbContext db, int tenantId, ILogger logger)
    {
        if (!await db.Settings.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId))
        {
            db.Settings.Add(new GlobalSettings { TenantId = tenantId, WrapUpSeconds = 30 });
            await db.SaveChangesAsync();
            logger.LogInformation("Instellingen geseed (tenant {TenantId}, nawerktijd: 30s)", tenantId);
        }
    }

    private static List<OpeningHoursWindow> AllDayOpeningHours() =>
        Enum.GetValues<DayOfWeek>()
            .Select(day => new OpeningHoursWindow
            {
                Day = day,
                Opens = new TimeOnly(0, 0),
                Closes = new TimeOnly(23, 59, 59),
            })
            .ToList();

    /// <summary>
    /// Genereert ontbrekende TTS-prompts voor wachtrijen met een ingestelde tekst (bv. na een verse
    /// sounds-volume of seed). Idempotent: bestaande bestanden worden hergebruikt. No-op zonder TTS.
    /// </summary>
    private static async Task RegeneratePromptsAsync(CcDbContext db, ITtsService tts, ILogger logger)
    {
        if (!tts.IsEnabled) return;

        // Draait zonder tenant-context: bewust over alle tenants heen.
        var queues = await db.Queues.IgnoreQueryFilters().ToListAsync();
        var changed = 0;
        foreach (var q in queues)
        {
            if (await EnsurePromptAsync(tts, q.WelcomeText, q.Voice, $"queue-{q.Name}-welcome") is { } w)
            { q.WelcomePrompt = w; changed++; }
            if (await EnsurePromptAsync(tts, q.ClosedText, q.Voice, $"queue-{q.Name}-closed") is { } c)
            { q.ClosedPrompt = c; changed++; }
        }
        if (changed > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("TTS: {Count} prompt(s) gegenereerd/gekoppeld bij opstart", changed);
        }
    }

    /// <summary>Zorgt dat de prompt bestaat; geeft de "sound:custom/..."-referentie of null als er niets te doen valt.</summary>
    private static async Task<string?> EnsurePromptAsync(ITtsService tts, string text, string voice, string outputName)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (tts.OutputExists(outputName) || await tts.SynthesizeAsync(text, voice, outputName))
            return $"sound:custom/{outputName}";
        return null;
    }
}
