namespace ContactCenter.Api.Data;

/// <summary>Eén openingsvenster op één weekdag; meerdere vensters per dag zijn toegestaan.</summary>
public class OpeningHoursWindow
{
    public int Id { get; set; }
    public int QueueConfigId { get; set; }
    public DayOfWeek Day { get; set; }
    public TimeOnly Opens { get; set; }
    public TimeOnly Closes { get; set; }
}
