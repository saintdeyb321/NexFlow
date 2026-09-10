using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Abstractions;

public interface ICatalogGenerationUsageRepository
{
    Task<CatalogGenerationUsage?> GetUsageForTodayAsync(Guid workspaceId, DateTime date, CancellationToken cancellationToken);
    Task SaveUsageAsync(CatalogGenerationUsage usage, CancellationToken cancellationToken);
}