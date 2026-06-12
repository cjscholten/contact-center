namespace ContactCenter.Api.Ami;

/// <summary>Beheer van wachtrijleden; app_queue is hiervoor alleen via AMI te besturen.</summary>
public interface IQueueMemberControl
{
    Task AddAsync(string queue, string endpoint, string memberName, CancellationToken ct = default);

    Task RemoveAsync(string queue, string endpoint, CancellationToken ct = default);

    /// <summary>Pauzeert of hervat het lid in álle wachtrijen waar het in zit.</summary>
    Task SetPausedAsync(string endpoint, bool paused, string reason, CancellationToken ct = default);
}
