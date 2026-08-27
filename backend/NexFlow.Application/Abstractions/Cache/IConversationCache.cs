using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Abstractions.Cache;

public class ConversationContextDto
{
    public string? CurrentIntent { get; set; }

    // 🔥 SPRINT 7: Contexto Multi-Sede robusto
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
    Task<bool> TryAcquireMessageLockAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
}