namespace ContactCenter.Api.Data;

/// <summary>Inkomend nummer (E.164, met +) dat naar een wachtrij routeert.</summary>
public class InboundNumber
{
    public int Id { get; set; }
    public required string Number { get; set; }
    public int QueueConfigId { get; set; }
}
