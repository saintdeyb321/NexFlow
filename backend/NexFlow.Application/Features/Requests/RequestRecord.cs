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

// 🔥 SPRINT 06: Tipos específicos de solicitudes requeridos por la auditoría
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
    public string ConversationId { get; set; } = string.Empty; // Vinculación con la conversación original
    public string ConsumerPhone { get; set; } = string.Empty;

    public RequestType Type { get; set; } = RequestType.Other;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public string? AssignedTo { get; set; } // Asignación a un humano

    public Dictionary<string, object> Metadata { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}