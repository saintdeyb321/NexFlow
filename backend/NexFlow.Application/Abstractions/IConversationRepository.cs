using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Abstractions;

public interface IConversationRepository
{
    Task<ConversationRecord?> GetActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken);
    Task<ConversationRecord> GetOrCreateActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken);

    Task CreateConversationAsync(Guid workspaceId, ConversationRecord conversation, CancellationToken cancellationToken);
    Task DeleteConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken);

    // 🔥 Sprint 4.1: Se añade HandoffReason
    Task UpdateConversationModeAsync(Guid workspaceId, string conversationId, ConversationMode mode, HandoffReason reason, CancellationToken cancellationToken);

    Task AddMessageAsync(Guid workspaceId, string conversationId, MessageRecord message, CancellationToken cancellationToken);

    // 🔥 Sprint 4.1: Actualizar a Sent o Failed
    Task UpdateMessageStatusAsync(Guid workspaceId, string conversationId, string messageId, MessageStatus status, string? externalMessageId, CancellationToken cancellationToken);

    Task<IEnumerable<ConversationRecord>> GetRecentConversationsAsync(Guid workspaceId, int limit, CancellationToken cancellationToken);
    Task<IEnumerable<MessageRecord>> GetMessagesAsync(Guid workspaceId, string conversationId, int limit, CancellationToken cancellationToken);
    Task<ConversationRecord?> GetConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken);
}

public interface IConsumerIdentityRepository
{
    Task<ConsumerIdentityRecord?> GetConsumerAsync(Guid workspaceId, string phone, CancellationToken cancellationToken);
    Task UpsertConsumerAsync(Guid workspaceId, ConsumerIdentityRecord consumer, CancellationToken cancellationToken);
}