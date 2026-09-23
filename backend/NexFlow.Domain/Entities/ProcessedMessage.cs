namespace NexFlow.Domain.Entities;

public class ProcessedMessage
{
    public Guid WorkspaceId { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string Status { get; set; } = "PROCESSING"; // PROCESSING, PROCESSED, FAILED
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; } = 1;
    public string? LastError { get; set; }
}