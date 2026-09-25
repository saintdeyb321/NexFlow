using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Services.DTOs;

namespace NexFlow.Application.Features.Business.Offerings;

public sealed class OfferingService : IOfferingService
{
    private readonly ICatalogRepository _catalogRepo;

    public OfferingService(ICatalogRepository catalogRepo)
    {
        _catalogRepo = catalogRepo;
    }

    public async Task<IEnumerable<ProductDto>> GetProductsAsync(Guid workspaceId, string? locationId, string? query, CancellationToken ct)
    {
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);

        var products = items
            .Where(i => i.Type == "PRODUCT")
            .Select(MapToSpecific<ProductDto>);

        return FilterItems(products, locationId, query);
    }

    public async Task<IEnumerable<ServiceDto>> GetServicesAsync(Guid workspaceId, string? locationId, string? query, CancellationToken ct)
    {
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);

        var services = items
            .Where(i => i.Type == "SERVICE")
            .Select(MapToSpecific<ServiceDto>);

        return FilterItems(services, locationId, query);
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid workspaceId, string productId, CancellationToken ct)
    {
        var item = await _catalogRepo.GetItemByIdAsync(workspaceId, productId, ct);
        if (item == null || item.Type != "PRODUCT") return null;
        return MapToSpecific<ProductDto>(item);
    }

    public async Task<ServiceDto?> GetServiceByIdAsync(Guid workspaceId, string serviceId, CancellationToken ct)
    {
        var item = await _catalogRepo.GetItemByIdAsync(workspaceId, serviceId, ct);
        if (item == null || item.Type != "SERVICE") return null;
        return MapToSpecific<ServiceDto>(item);
    }

    public async Task<bool> IsServiceAvailableAtLocationAsync(Guid workspaceId, string serviceId, string locationId, CancellationToken ct)
    {
        var item = await GetServiceByIdAsync(workspaceId, serviceId, ct);
        if (item == null || !item.IsActive) return false;

        if (string.Equals(item.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase))
            return true;

        return item.LocationIds != null && item.LocationIds.Contains(locationId);
    }

    private IEnumerable<T> FilterItems<T>(IEnumerable<T> items, string? locationId, string? query) where T : BusinessOfferingDto
    {
        if (!string.IsNullOrWhiteSpace(locationId))
        {
            items = items.Where(i =>
                string.Equals(i.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase) ||
                (i.LocationIds != null && i.LocationIds.Contains(locationId)));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.ToLowerInvariant();
            items = items.Where(i =>
                i.Name.ToLowerInvariant().Contains(term) ||
                (i.Description != null && i.Description.ToLowerInvariant().Contains(term)));
        }

        return items;
    }

    private static T MapToSpecific<T>(BusinessOfferingDto baseItem) where T : BusinessOfferingDto
    {
        var json = JsonSerializer.Serialize(baseItem);
        return JsonSerializer.Deserialize<T>(json)!;
    }
}