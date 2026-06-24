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

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await db.Database.MigrateAsync();
                await SeedAsync(db, logger);
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
}
