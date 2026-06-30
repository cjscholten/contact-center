using ContactCenter.Api.Data;

namespace ContactCenter.Api.CallFlow;

/// <summary>Pure beslislogica: wat doen we met een gesprek voor deze wachtrij op dit moment?</summary>
public sealed class QueueDecisionService
{
    public CallAction Decide(QueueConfig queue, DateTimeOffset utcNow)
    {
        if (queue.AdHocClosed)
            return string.IsNullOrWhiteSpace(queue.AdHocForwardNumber)
                ? new PlayAndHangup(queue.ClosedPrompt)
                : new ForwardTo(queue.AdHocForwardNumber);

        return IsOpen(queue, utcNow)
            ? new RouteToQueue(queue.TenantId, queue.Name, queue.WelcomePrompt)
            : new PlayAndHangup(queue.ClosedPrompt);
    }

    /// <summary>Valt het huidige moment binnen een openingsvenster? (negeert ad-hoc sluiting.)</summary>
    public bool IsOpen(QueueConfig queue, DateTimeOffset utcNow)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(queue.TimeZone);
        var local = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var time = TimeOnly.FromTimeSpan(local.TimeOfDay);

        return queue.OpeningHours.Any(w =>
            w.Day == local.DayOfWeek && w.Opens <= time && time < w.Closes);
    }
}
