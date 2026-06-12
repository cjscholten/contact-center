namespace ContactCenter.Api.Data;

/// <summary>Koppeling: deze agent werkt in deze wachtrij.</summary>
public class AgentQueueAssignment
{
    public int AgentId { get; set; }
    public int QueueConfigId { get; set; }
    public QueueConfig? Queue { get; set; }
}
