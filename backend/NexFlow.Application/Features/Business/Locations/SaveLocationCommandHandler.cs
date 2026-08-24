using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
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
        var existingLocations = await _locationRepository.GetLocationsAsync(request.WorkspaceId, cancellationToken);

        bool isNewLocation = string.IsNullOrEmpty(request.Location.Id) || !existingLocations.Any(l => l.Id == request.Location.Id);

        if (isNewLocation)
        {
            int maxLocations = await _entitlementService.GetMaxLocationsAsync(request.WorkspaceId, cancellationToken);
            if (existingLocations.Count() >= maxLocations)
            {
                return Result.Failure(new Error("Location.LimitReached", $"Límite alcanzado. Tu licencia actual solo permite un máximo de {maxLocations} sede(s)."));
            }
        }

        await _locationRepository.SaveLocationAsync(request.WorkspaceId, request.Location, cancellationToken);
        return Result.Success();
    }
}