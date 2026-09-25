using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Services.DTOs;

namespace NexFlow.Application.Features.Business.Offerings;

public interface IOfferingService
{
    // Rutas Módulo Catálogo
    Task<IEnumerable<ProductDto>> GetProductsAsync(Guid workspaceId, string? locationId, string? query, CancellationToken ct);
    Task<ProductDto?> GetProductByIdAsync(Guid workspaceId, string productId, CancellationToken ct);

    // Rutas Módulo Servicios
    Task<IEnumerable<ServiceDto>> GetServicesAsync(Guid workspaceId, string? locationId, string? query, CancellationToken ct);
    Task<ServiceDto?> GetServiceByIdAsync(Guid workspaceId, string serviceId, CancellationToken ct);
    Task<bool> IsServiceAvailableAtLocationAsync(Guid workspaceId, string serviceId, string locationId, CancellationToken ct);
}