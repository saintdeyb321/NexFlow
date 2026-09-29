namespace NexFlow.Domain.Entities.System;

public enum TenantDeletionStatus { Pending, Processing, Completed, Failed }
public class TenantDeletionJob
{
    public Guid WorkspaceId { get; set; }
    public Guid RequestedBy { get; set; }
    public TenantDeletionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseUntil { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
}
