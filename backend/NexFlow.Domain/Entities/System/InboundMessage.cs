namespace NexFlow.Domain.Entities.System;

public enum InboundMessageStatus { Pending, Processing, Processed, Failed, DeadLetter }

public class InboundMessage
{
    public Guid Id { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string ExternalMessageId { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public InboundMessageStatus Status { get; set; } = InboundMessageStatus.Pending;
    public int Attempts { get; set; } = 0;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public string? LastError { get; set; }
}