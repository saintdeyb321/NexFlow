using NexFlow.Application.Features.Knowledge;

namespace NexFlow.Application.Abstractions;

public interface IKnowledgeService
{
    Task<BusinessKnowledgeSnapshot> GetSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken);

    // 🔥 SPRINT 04: Query ahora es asíncrono para poder ir a BD y no consumir memoria.
    Task<KnowledgeResult> QueryAsync(Guid workspaceId, BusinessKnowledgeSnapshot snapshot, KnowledgeQuery query, CancellationToken cancellationToken);
}