namespace NexFlow.Domain.Entities.System;

public enum OutboxStatus
{
    Pending,
    Processed,
    Failed
}

public class OutboxMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid WorkspaceId { get; set; }
    public string EventType { get; set; } = string.Empty;

    // Guardamos el N8nEventPayload serializado a JSON para que sea agnóstico
    public string PayloadJson { get; set; } = string.Empty;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; } = 0;
}