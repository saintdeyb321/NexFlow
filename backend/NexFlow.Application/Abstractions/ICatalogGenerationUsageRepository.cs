using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Abstractions;

public interface ICatalogGenerationUsageRepository
{
    Task IncrementUsageAtomicallyAsync(Guid workspaceId, DateTime date, CancellationToken cancellationToken);
}