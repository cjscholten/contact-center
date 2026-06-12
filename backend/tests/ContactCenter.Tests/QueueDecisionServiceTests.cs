using ContactCenter.Api.CallFlow;
using ContactCenter.Api.Data;

namespace ContactCenter.Tests;

public class QueueDecisionServiceTests
{
    private readonly QueueDecisionService _sut = new();

    // Woensdag 10 juni 2026, 12:00 UTC = 14:00 in Europe/Amsterdam (CEST)
    private static readonly DateTimeOffset WednesdayNoonUtc =
        new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private static QueueConfig Queue(params OpeningHoursWindow[] windows) => new()
    {
        Name = "support",
        DisplayName = "Support",
        WelcomePrompt = "sound:welkom",
        ClosedPrompt = "sound:gesloten",
        OpeningHours = [.. windows],
    };

    private static OpeningHoursWindow Window(DayOfWeek day, int opensHour, int closesHour) => new()
    {
        Day = day,
        Opens = new TimeOnly(opensHour, 0),
        Closes = new TimeOnly(closesHour, 0),
    };

    [Fact]
    public void Binnen_openingstijden_gaat_naar_wachtrij_met_welkomsttekst()
    {
        var queue = Queue(Window(DayOfWeek.Wednesday, 9, 17));

        var action = _sut.Decide(queue, WednesdayNoonUtc);

        Assert.Equal(new RouteToQueue("support", "sound:welkom"), action);
    }

    [Fact]
    public void Buiten_openingstijden_speelt_gesloten_tekst_en_hangt_op()
    {
        var queue = Queue(Window(DayOfWeek.Wednesday, 9, 13));

        var action = _sut.Decide(queue, WednesdayNoonUtc);

        Assert.Equal(new PlayAndHangup("sound:gesloten"), action);
    }

    [Fact]
    public void Openingstijden_gelden_in_lokale_tijdzone_niet_in_utc()
    {
        // 07:30 UTC = 09:30 CEST: open volgens lokale tijd, gesloten als het UTC zou zijn
        var queue = Queue(Window(DayOfWeek.Wednesday, 9, 17));
        var earlyUtc = new DateTimeOffset(2026, 6, 10, 7, 30, 0, TimeSpan.Zero);

        var action = _sut.Decide(queue, earlyUtc);

        Assert.IsType<RouteToQueue>(action);
    }

    [Fact]
    public void Andere_weekdag_telt_niet_als_open()
    {
        var queue = Queue(Window(DayOfWeek.Tuesday, 0, 23));

        var action = _sut.Decide(queue, WednesdayNoonUtc);

        Assert.Equal(new PlayAndHangup("sound:gesloten"), action);
    }

    [Fact]
    public void Meerdere_vensters_op_een_dag_worden_allemaal_gecontroleerd()
    {
        var queue = Queue(
            Window(DayOfWeek.Wednesday, 9, 12),
            Window(DayOfWeek.Wednesday, 13, 17));

        var action = _sut.Decide(queue, WednesdayNoonUtc);

        Assert.IsType<RouteToQueue>(action); // 14:00 lokaal valt in het middagvenster
    }

    [Fact]
    public void AdHoc_gesloten_wint_van_openingstijden()
    {
        var queue = Queue(Window(DayOfWeek.Wednesday, 0, 23));
        queue.AdHocClosed = true;

        var action = _sut.Decide(queue, WednesdayNoonUtc);

        Assert.Equal(new PlayAndHangup("sound:gesloten"), action);
    }

    [Fact]
    public void AdHoc_gesloten_met_doorschakelnummer_schakelt_door()
    {
        var queue = Queue(Window(DayOfWeek.Wednesday, 0, 23));
        queue.AdHocClosed = true;
        queue.AdHocForwardNumber = "+31201234567";

        var action = _sut.Decide(queue, WednesdayNoonUtc);

        Assert.Equal(new ForwardTo("+31201234567"), action);
    }
}
