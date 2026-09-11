using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Abstractions;

public interface ICatalogArtifactRepository
{
    Task<CatalogArtifact?> GetCurrentArtifactAsync(Guid workspaceId, string scope, CancellationToken cancellationToken);
    Task SaveArtifactAsync(CatalogArtifact artifact, CancellationToken cancellationToken);
}