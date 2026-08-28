using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;


namespace NexFlow.Application.Abstractions;

public interface IConversationRepository
{
    Task<ConversationRecord?> GetActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken);
    Task<ConversationRecord> GetOrCreateActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken);

    Task CreateConversationAsync(Guid workspaceId, ConversationRecord conversation, CancellationToken cancellationToken);

    Task UpdateConversationModeAsync(Guid workspaceId, string conversationId, ConversationMode mode, CancellationToken cancellationToken);

    Task AddMessageAsync(Guid workspaceId, string conversationId, MessageRecord message, CancellationToken cancellationToken);

    Task<IEnumerable<ConversationRecord>> GetRecentConversationsAsync(Guid workspaceId, int limit, CancellationToken cancellationToken);
    Task<IEnumerable<MessageRecord>> GetMessagesAsync(Guid workspaceId, string conversationId, int limit, CancellationToken cancellationToken);
    Task<ConversationRecord?> GetConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken);
}

public interface IConsumerIdentityRepository
{
    Task<ConsumerIdentityRecord?> GetConsumerAsync(Guid workspaceId, string phone, CancellationToken cancellationToken);
    Task UpsertConsumerAsync(Guid workspaceId, ConsumerIdentityRecord consumer, CancellationToken cancellationToken);
}