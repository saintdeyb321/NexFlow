using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Abstractions;

public interface ICatalogGenerationUsageRepository
{
    // Artifact claim and daily reservation belong to the same Firestore transaction.
    Task<CatalogArtifact> ReserveGenerationAsync(CatalogArtifact artifact, DateTime date, CancellationToken cancellationToken);
}
