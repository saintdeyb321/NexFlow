namespace NexFlow.Application.Features.Knowledge;

public interface IKnowledgeService
{
    Task<BusinessKnowledgeSnapshot> GetSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken);

    /// <summary>
    /// Consulta el conocimiento estructurado sin usar IA, aplicando filtros de sede y términos de búsqueda.
    /// </summary>
    KnowledgeResult Query(BusinessKnowledgeSnapshot snapshot, KnowledgeQuery query);
}