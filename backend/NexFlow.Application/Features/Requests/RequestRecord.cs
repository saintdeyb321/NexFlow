namespace NexFlow.Application.Features.Requests;

public enum RequestStatus
{
    Pending,
    InReview,
    Approved,
    Rejected,
    Completed,
    Cancelled
}

public enum RequestType
{
    Tramite,
    CommercialInquiry,
    Support,
    HumanHandoff,
    Other
}

public class RequestRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string ConsumerPhone { get; set; } = string.Empty;

    public RequestType Type { get; set; } = RequestType.Other;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public string? AssignedTo { get; set; }

    // 🔥 SPRINT 08: Clave de idempotencia para evitar solicitudes duplicadas por reintentos de red
    public string? SourceMessageId { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}