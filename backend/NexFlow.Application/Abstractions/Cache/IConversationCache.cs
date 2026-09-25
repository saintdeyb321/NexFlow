namespace NexFlow.Application.Abstractions.Cache;

public class ConversationContextDto
{
    // 🔥 SPRINT 07: Nuevos campos para gestionar el Takeover sin borrar la memoria
    public string Mode { get; set; } = "Automatic"; // Automatic, Human
    public string? HandoffReason { get; set; }
    public DateTime? HandoffAt { get; set; }

    // 1. Estado de la Transacción
    public string? CurrentGoal { get; set; }
    public string? CurrentStep { get; set; }

    // 2. Entidades Recolectadas (Memoria)
    public string? SelectedServiceId { get; set; }
    public string? SelectedLocationId { get; set; }
    public string? TargetDate { get; set; }
    public string? TargetTime { get; set; }
    public string? RealCustomerName { get; set; }

    public List<string> MissingFields { get; set; } = new();
    public string? LastQuestion { get; set; }
    public string? LastIntent { get; set; }
    public double? Confidence { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public interface IConversationCache
{
    Task SetContextAsync(Guid workspaceId, string customerPhone, ConversationContextDto context, CancellationToken cancellationToken);
    Task<ConversationContextDto?> GetContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken);
    Task DeleteContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken);
    Task MarkMessageAsAiGeneratedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
    Task<bool> IsMessageAiGeneratedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
}