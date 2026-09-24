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
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        if (!locations.Any(l => l.Id == locationId)) return false;

        var offering = await _catalogRepository.GetItemByIdAsync(workspaceId, offeringId, cancellationToken);
        if (offering == null || !offering.IsActive) return false;

        return IsOfferingAvailableAtLocation(offering, locationId);
    }

    // 🔥 SPRINT 1: Reemplazo por clase base
    public bool IsOfferingAvailableAtLocation(BusinessOfferingDto? offering, string locationId)
    {
        if (offering == null) return false; // Protección contra nulos
        if (string.Equals(offering.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(locationId)) return false;
        return offering.LocationIds != null && offering.LocationIds.Contains(locationId);
    }
}