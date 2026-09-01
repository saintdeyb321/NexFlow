using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Abstractions;

public interface ILocationRepository
{
    Task<IEnumerable<LocationDto>> GetLocationsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveLocationAsync(Guid workspaceId, LocationDto location, CancellationToken cancellationToken);
    Task DeleteLocationAsync(Guid workspaceId, string locationId, CancellationToken cancellationToken);
}