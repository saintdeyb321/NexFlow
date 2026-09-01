using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Abstractions;

public interface IServiceRepository
{
    Task<IEnumerable<ServiceDto>> GetServicesAsync(Guid workspaceId, CancellationToken cancellationToken);

    // Nuevas firmas determinísticas
    Task<IEnumerable<ServiceDto>> GetActiveServicesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<ServiceDto?> GetServiceByIdAsync(Guid workspaceId, string serviceId, CancellationToken cancellationToken);
    Task<IEnumerable<ServiceDto>> GetServicesByCategoryAsync(Guid workspaceId, string category, CancellationToken cancellationToken);

    Task<ServiceDto> SaveServiceAsync(Guid workspaceId, ServiceDto service, CancellationToken cancellationToken);
    Task DeleteServiceAsync(Guid workspaceId, string serviceId, CancellationToken cancellationToken);
}