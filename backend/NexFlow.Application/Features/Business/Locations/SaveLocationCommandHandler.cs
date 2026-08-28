using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;

namespace NexFlow.Application.Features.Business.Locations;

public record SaveLocationCommand(Guid WorkspaceId, LocationDto Location);

public class SaveLocationCommandHandler
{
    private readonly ILocationRepository _locationRepository;
    private readonly IEntitlementService _entitlementService;

    public SaveLocationCommandHandler(ILocationRepository locationRepository, IEntitlementService entitlementService)
    {
        _locationRepository = locationRepository;
        _entitlementService = entitlementService;
    }

    public async Task<Result> Handle(SaveLocationCommand request, CancellationToken cancellationToken)
    {
        var existingLocations = (await _locationRepository.GetLocationsAsync(request.WorkspaceId, cancellationToken)).ToList();

        bool isNewLocation = string.IsNullOrEmpty(request.Location.Id) || !existingLocations.Any(l => l.Id == request.Location.Id);
        if (isNewLocation)
        {
            int maxLocations = await _entitlementService.GetMaxLocationsAsync(request.WorkspaceId, cancellationToken);
            if (existingLocations.Count >= maxLocations)
            {
                return Result.Failure(new Error("Location.LimitReached", $"Límite alcanzado. Tu licencia actual solo permite un máximo de {maxLocations} sede(s)."));
            }
        }

        var locationToSave = request.Location;

        if (locationToSave.IsMain)
        {
            foreach (var loc in existingLocations)
            {
                if (loc.IsMain && loc.Id != locationToSave.Id)
                {
                    var updatedLoc = loc with { IsMain = false };
                    await _locationRepository.SaveLocationAsync(request.WorkspaceId, updatedLoc, cancellationToken);
                }
            }
        }
        else if (!existingLocations.Any(l => l.IsMain) && isNewLocation)
        {
            locationToSave = locationToSave with { IsMain = true };
        }

        await _locationRepository.SaveLocationAsync(request.WorkspaceId, locationToSave, cancellationToken);
        return Result.Success();
    }
}