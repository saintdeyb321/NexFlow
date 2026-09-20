using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Features.Business.LocationAvailability;

public class LocationAvailabilityService : ILocationAvailabilityService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILocationRepository _locationRepository;

    public LocationAvailabilityService(ICatalogRepository catalogRepository, ILocationRepository locationRepository)
    {
        _catalogRepository = catalogRepository;
        _locationRepository = locationRepository;
    }

    public async Task<bool> IsOfferingAvailableAtLocationAsync(Guid workspaceId, string offeringId, string locationId, CancellationToken cancellationToken)
    {
        // 1. Validar que la sede pertenece al workspace
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        if (!locations.Any(l => l.Id == locationId)) return false;

        // 2. Validar que la oferta (producto/servicio) pertenece al workspace
        var offering = await _catalogRepository.GetItemByIdAsync(workspaceId, offeringId, cancellationToken);
        if (offering == null || !offering.IsActive) return false;

        // 3. Validar disponibilidad estricta
        return IsOfferingAvailableAtLocation(offering, locationId);
    }

    public bool IsOfferingAvailableAtLocation(CatalogItemDto offering, string locationId)
    {
        if (string.Equals(offering.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(locationId)) return false;
        return offering.LocationIds != null && offering.LocationIds.Contains(locationId);
    }
}