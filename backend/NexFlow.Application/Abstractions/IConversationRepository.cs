using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Abstractions;

public interface IConversationRepository
{
    Task<(int Conversations, int? Handoffs)> CountForPeriodAsync(Guid workspaceId, DateTime start, DateTime end, CancellationToken ct);
    Task<(MessageRecord Message, bool SendRequired)> PrepareOutboundAsync(Guid workspaceId, string conversationId, string phone, MessageRecord message, CancellationToken cancellationToken);
    Task ConfirmOutboundAsync(Guid workspaceId, string messageId, string externalMessageId, CancellationToken cancellationToken);
    Task<bool> StartOutboundAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
    Task RecordOutboundFailureAsync(Guid workspaceId, string messageId, string error, MessageStatus status, CancellationToken cancellationToken, string? providerConfirmationId = null);
    Task<OutboundReconciliationResult> ReconcileOutboundEchoAsync(Guid workspaceId, string? conversationId, string phone, string content, string externalMessageId, DateTime observedAt, CancellationToken cancellationToken);
    Task<(MessageRecord Message, string ConversationId, string Phone)?> GetOutboundAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
    Task<MessageOrigin?> GetMessageOriginAsync(Guid workspaceId, string phone, string externalMessageId, CancellationToken cancellationToken);
    Task<bool> HasUnconfirmedOutboundAsync(Guid workspaceId, string phone, CancellationToken cancellationToken);

    Task<MessageRecord?> GetMessageByExternalIdAsync(Guid workspaceId, string conversationId, string externalMessageId, CancellationToken cancellationToken);

    Task<ConversationRecord?> GetActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken);
    Task<ConversationRecord> GetOrCreateActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken);
    Task CreateConversationAsync(Guid workspaceId, ConversationRecord conversation, CancellationToken cancellationToken);
    Task DeleteConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken);
    Task CloseConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken);
    Task SetHandoffAsync(Guid workspaceId, string conversationId, string phone, ConversationMode mode, HandoffReason reason, CancellationToken cancellationToken);
    Task AddMessageAsync(Guid workspaceId, string conversationId, MessageRecord message, CancellationToken cancellationToken);
    Task<IEnumerable<ConversationRecord>> GetRecentConversationsAsync(Guid workspaceId, int limit, CancellationToken cancellationToken, DateTime? after = null, string? afterId = null);
    Task<IEnumerable<MessageRecord>> GetMessagesAsync(Guid workspaceId, string conversationId, int limit, CancellationToken cancellationToken, DateTime? after = null, string? afterId = null);
    Task<IEnumerable<MessageRecord>> GetMessagesByIdsAsync(Guid workspaceId, string conversationId, IReadOnlyCollection<string> ids, CancellationToken cancellationToken);
    Task<ConversationRecord?> GetConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken);
}

public interface IConsumerIdentityRepository
{
    Task<ConsumerIdentityRecord?> GetConsumerAsync(Guid workspaceId, string phone, CancellationToken cancellationToken);
    Task UpsertConsumerAsync(Guid workspaceId, ConsumerIdentityRecord consumer, CancellationToken cancellationToken);
}
