using NexFlow.Application.Abstractions.Cache;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IConversationStateRepository
{
    Task<ConversationContextDto?> GetStateAsync(Guid workspaceId, string phone, CancellationToken cancellationToken);
    Task UpsertStateAsync(Guid workspaceId, string phone, ConversationContextDto state, CancellationToken cancellationToken);
    Task DeleteStateAsync(Guid workspaceId, string phone, CancellationToken cancellationToken);
}