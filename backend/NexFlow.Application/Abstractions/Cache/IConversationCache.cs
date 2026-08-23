using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Abstractions.Cache;

// NUEVO: La estructura de memoria a corto plazo
public class ConversationContextDto
{
    public string? CurrentIntent { get; set; }
    public string? LocationId { get; set; }
    public string? ServiceId { get; set; }
    public string? PendingAction { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public interface IConversationCache
{
    // Cambiamos el string simple por el objeto estructurado
    Task SetContextAsync(Guid workspaceId, string customerPhone, ConversationContextDto context, CancellationToken cancellationToken);
    Task<ConversationContextDto?> GetContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken);

    Task<bool> TryAcquireMessageLockAsync(string messageId, CancellationToken cancellationToken);
}