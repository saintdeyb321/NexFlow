namespace NexFlow.Application.Abstractions.Cache;

public class ConversationContextDto
{
    public string? CurrentIntent { get; set; }
    public string? SelectedLocationId { get; set; }
    public string? SelectedServiceId { get; set; }
    public string? PendingDate { get; set; }
    public string? PendingTime { get; set; }
    public string? PendingAction { get; set; }
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