namespace NexFlow.Application.Abstractions.Cache;

public class ConversationContextDto
{
    public string? SelectedLocationId { get; set; }
    public string? CurrentGoal { get; set; } // Ej: "BOOKING", "SUPPORT", "INFO"
    public string? SelectedServiceId { get; set; }
    public string? TargetDate { get; set; }
    public string? TargetTime { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public interface IConversationCache
{
    Task SetContextAsync(Guid workspaceId, string customerPhone, ConversationContextDto context, CancellationToken cancellationToken);
    Task<ConversationContextDto?> GetContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken);
    Task DeleteContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken);

    // (Opcional, según lo requiera tu webhook)
    Task MarkMessageAsAiGeneratedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
    Task<bool> IsMessageAiGeneratedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
}