using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Abstractions;

public interface ICatalogArtifactRepository
{
    Task<CatalogArtifact?> GetCurrentArtifactAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveArtifactAsync(CatalogArtifact artifact, CancellationToken cancellationToken);
}