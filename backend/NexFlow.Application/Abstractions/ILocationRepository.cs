using NexFlow.Application.Features.Business;
using NexFlow.Application.Common;

namespace NexFlow.Application.Abstractions;

public interface ILocationRepository
{
    Task<IEnumerable<LocationDto>> GetLocationsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<Result> SaveLocationAsync(Guid workspaceId, LocationDto location, bool create, int maxLocations, CancellationToken cancellationToken);
    Task<Result> DeleteLocationAsync(Guid workspaceId, string locationId, CancellationToken cancellationToken);
}
