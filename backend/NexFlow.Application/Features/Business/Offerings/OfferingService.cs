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
            // 🔥 SPRINT 2: Solo entidades SERVICE activas del workspace consultado.
            .Where(i => i.Type == "SERVICE" && i.IsActive)
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
        // 🔥 SPRINT 2: No reutilizamos selecciones inactivas ni entidades de otro tipo.
        if (item == null || item.Type != "SERVICE" || !item.IsActive) return null;
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
        return baseItem switch
        {
            T typedItem => typedItem,
            _ => throw new InvalidOperationException(
                $"El repositorio debe devolver {typeof(T).Name} con sus propiedades derivadas para {baseItem.Id}.")
        };
    }
}

// 🔥 SPRINT 2: Extendemos el contrato existente sin modificar archivos adicionales.
public static class OfferingServiceReservationExtensions
{
    public static async Task<(ServiceDto? Service, string? Clarification)> ResolveReservationServiceAsync(
        this IOfferingService offerings, Guid workspaceId, string locationId, string serviceName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(locationId))
            return (null, "¿En qué sede deseas reservar?");

        if (string.IsNullOrWhiteSpace(serviceName))
            return (null, "¿Qué servicio deseas reservar en esa sede?");

        // 🔥 SPRINT 2: La consulta al repositorio está acotada al workspace y a la sede.
        var services = await offerings.GetServicesAsync(workspaceId, locationId, null, ct);
        var matches = services
            .Where(s => s.Type == "SERVICE" && s.IsActive && s.RequiresReservation)
            .Where(s => string.Equals(s.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase)
                || (s.LocationIds != null && s.LocationIds.Contains(locationId)))
            .Where(s => string.Equals(s.Name.Trim(), serviceName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        return matches.Count switch
        {
            1 => (matches[0], null),
            0 => (null, "No encontré un servicio reservable con ese nombre en la sede seleccionada. ¿Podrías indicar el nombre exacto del servicio?"),
            _ => (null, "Hay varios servicios con ese nombre en la sede seleccionada. ¿Podrías precisar cuál necesitas o elegir otra sede?")
        };
    }
}
