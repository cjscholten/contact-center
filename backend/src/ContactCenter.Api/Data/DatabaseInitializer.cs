using ContactCenter.Api.Tts;
using Microsoft.EntityFrameworkCore;

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

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await db.Database.MigrateAsync();
                await SeedAsync(db, logger);
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

    private static async Task SeedAsync(CcDbContext db, ILogger logger)
    {
        if (!await db.Queues.AnyAsync())
        {
            var support = new QueueConfig
            {
                Name = "support",
                DisplayName = "Support",
                WelcomeText = "Welkom bij de klantenservice. Een moment geduld alstublieft, u wordt zo snel mogelijk geholpen.",
                Voice = "nl_NL-pim-medium",
                Numbers = [new InboundNumber { Number = "+19205008321" }],
                OpeningHours = Enum.GetValues<DayOfWeek>()
                    .Select(day => new OpeningHoursWindow
                    {
                        Day = day,
                        Opens = new TimeOnly(0, 0),
                        Closes = new TimeOnly(23, 59, 59),
                    })
                    .ToList(),
            };

            db.Queues.Add(support);
            await db.SaveChangesAsync();
            logger.LogInformation("Wachtrij 'support' geseed met nummer +19205008321 (24/7 open)");
        }

        if (!await db.Agents.AnyAsync())
        {
            var queueIds = await db.Queues.ToDictionaryAsync(q => q.Name, q => q.Id);
            db.Agents.AddRange(
                new Agent
                {
                    Name = "agent1001",
                    DisplayName = "Agent 1001",
                    Endpoint = "PJSIP/agent1001",
                    QueueAssignments = [.. queueIds.Values.Select(id => new AgentQueueAssignment { QueueConfigId = id })],
                },
                new Agent
                {
                    Name = "agent1002",
                    DisplayName = "Agent 1002",
                    Endpoint = "PJSIP/agent1002",
                    QueueAssignments = [new AgentQueueAssignment { QueueConfigId = queueIds["support"] }],
                });
            await db.SaveChangesAsync();
            logger.LogInformation("Agents 'agent1001' en 'agent1002' geseed");
        }

        if (!await db.Settings.AnyAsync())
        {
            db.Settings.Add(new GlobalSettings { WrapUpSeconds = 30 });
            await db.SaveChangesAsync();
            logger.LogInformation("Globale instellingen geseed (nawerktijd: 30s)");
        }

        if (!await db.Contacts.AnyAsync())
        {
            db.Contacts.AddRange(
                new Contact { Name = "Receptie", Number = "+31201234500", Department = "Kantoor" },
                new Contact { Name = "Helpdesk tweede lijn", Number = "+31201234510", Department = "Support" },
                new Contact { Name = "Boekhouding", Number = "+31201234520", Department = "Finance" });
            await db.SaveChangesAsync();
            logger.LogInformation("Voorbeeldcontacten geseed");
        }
    }

    /// <summary>
    /// Genereert ontbrekende TTS-prompts voor wachtrijen met een ingestelde tekst (bv. na een verse
    /// sounds-volume of seed). Idempotent: bestaande bestanden worden hergebruikt. No-op zonder TTS.
    /// </summary>
    private static async Task RegeneratePromptsAsync(CcDbContext db, ITtsService tts, ILogger logger)
    {
        if (!tts.IsEnabled) return;

        var queues = await db.Queues.ToListAsync();
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
