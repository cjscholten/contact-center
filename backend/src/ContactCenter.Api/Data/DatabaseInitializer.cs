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
        if (await db.Queues.AnyAsync())
            return;

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
}
