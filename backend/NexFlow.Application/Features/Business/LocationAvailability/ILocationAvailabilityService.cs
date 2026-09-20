using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Features.Business.LocationAvailability;

public interface ILocationAvailabilityService
{
    // Valida asíncronamente desde BD (Location ∈ Workspace, Offering ∈ Workspace, Offering en Location)
    Task<bool> IsOfferingAvailableAtLocationAsync(Guid workspaceId, string offeringId, string locationId, CancellationToken cancellationToken);

    // Validación síncrona en memoria para cuando ya tienes el DTO cargado
    bool IsOfferingAvailableAtLocation(CatalogItemDto offering, string locationId);
}