using NexFlow.Application.Abstractions;

namespace NexFlow.Application.Features.Business.Offerings;

public sealed class OfferingService : IOfferingService
{
    private readonly ICatalogRepository _catalogRepo;

    public OfferingService(ICatalogRepository catalogRepo)
    {
        _catalogRepo = catalogRepo;
    }

    public async Task<IEnumerable<CatalogItemDto>> SearchOfferingsAsync(Guid workspaceId, string? locationId, string? type, string? query, CancellationToken ct)
    {
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);

        // 1. Filtro por tipo (PRODUCT o SERVICE)
        if (!string.IsNullOrWhiteSpace(type) && type != "ALL")
        {
            items = items.Where(i => string.Equals(i.Type, type, StringComparison.OrdinalIgnoreCase));
        }

        // 2. 🔥 SPRINT 2: Location Isolation (Aislamiento por Sede)
        if (!string.IsNullOrWhiteSpace(locationId))
        {
            items = items.Where(i =>
                string.Equals(i.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase) ||
                (i.LocationIds != null && i.LocationIds.Contains(locationId)));
        }

        // 3. Filtro por búsqueda de texto
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.ToLowerInvariant();
            items = items.Where(i =>
                i.Name.ToLowerInvariant().Contains(term) ||
                (i.Description != null && i.Description.ToLowerInvariant().Contains(term)));
        }

        return items;
    }

    public async Task<CatalogItemDto?> GetOfferingByIdAsync(Guid workspaceId, string itemId, CancellationToken ct)
    {
        return await _catalogRepo.GetItemByIdAsync(workspaceId, itemId, ct);
    }

    public async Task<bool> IsAvailableAtLocationAsync(Guid workspaceId, string itemId, string locationId, CancellationToken ct)
    {
        var item = await GetOfferingByIdAsync(workspaceId, itemId, ct);
        if (item == null || !item.IsActive) return false;

        if (string.Equals(item.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase))
            return true;

        return item.LocationIds != null && item.LocationIds.Contains(locationId);
    }
}