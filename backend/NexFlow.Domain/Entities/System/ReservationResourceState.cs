namespace NexFlow.Domain.Entities.System;

// SQL authority for reservation/configuration coordination. A pending operation
// never expires; NextAttemptAt schedules recovery, it does not unlock the resource.
public sealed class ReservationResourceState
{
    public Guid WorkspaceId { get; set; }
    public string Kind { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public long Version { get; set; }
    public Guid? PendingOperationId { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime NextAttemptAt { get; set; }
}
