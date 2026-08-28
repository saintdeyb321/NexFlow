using System;

namespace NexFlow.Domain.Entities;

public class ProcessedMessage
{
    public Guid WorkspaceId { get; set; }
    public string MessageId { get; set; } = null!;
    public DateTime ProcessedAt { get; set; }
}