// 🔥 NUEVO NAMESPACE
using NexFlow.Application.Features.Shared.DTOs;

namespace NexFlow.Application.Features.Business.LocationAvailability;

public interface ILocationAvailabilityService
{
    Task<bool> IsOfferingAvailableAtLocationAsync(Guid workspaceId, string offeringId, string locationId, CancellationToken cancellationToken);
    bool IsOfferingAvailableAtLocation(BusinessOfferingDto? offering, string locationId);
}