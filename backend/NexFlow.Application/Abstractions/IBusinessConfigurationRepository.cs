using NexFlow.Application.DTOs.Business;

namespace NexFlow.Application.Abstractions;

public interface IBusinessConfigurationRepository
{
    // Perfil del negocio
    Task<BusinessProfileDto?> GetProfileAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveProfileAsync(Guid workspaceId, BusinessProfileDto profile, CancellationToken cancellationToken);

    // Sedes
    Task<IEnumerable<LocationDto>> GetLocationsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveLocationAsync(Guid workspaceId, LocationDto location, CancellationToken cancellationToken);
    Task DeleteLocationAsync(Guid workspaceId, string locationId, CancellationToken cancellationToken);

    // Horarios (Puede ser global o por sede)
    Task<IEnumerable<BusinessHoursDto>> GetBusinessHoursAsync(Guid workspaceId, string? locationId, CancellationToken cancellationToken);
    Task SaveBusinessHoursAsync(Guid workspaceId, string? locationId, IEnumerable<BusinessHoursDto> hours, CancellationToken cancellationToken);
}