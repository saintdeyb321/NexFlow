namespace NexFlow.Application.Abstractions.Cache;

public class ConversationContextDto
{
    // 🔥 NEXFLOW 2.0: Memoria Limpia. Solo guardamos contexto real.
    public string? SelectedLocationId { get; set; }

    // Podremos expandir esto en el futuro si necesitamos rastrear carritos de compra o IDs de transacciones.
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