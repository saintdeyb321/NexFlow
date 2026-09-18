using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Features.Business.Offerings;

public interface IOfferingService
{
    Task<IEnumerable<CatalogItemDto>> SearchOfferingsAsync(Guid workspaceId, string? locationId, string? type, string? query, CancellationToken ct);
    Task<CatalogItemDto?> GetOfferingByIdAsync(Guid workspaceId, string itemId, CancellationToken ct);
    Task<bool> IsAvailableAtLocationAsync(Guid workspaceId, string itemId, string locationId, CancellationToken ct);
}