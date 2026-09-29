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

        // 🔥 SPRINT 10: Regla Férrea: Si no hay ninguna otra sede principal, ESTA debe ser la principal.
        var otherLocations = existingLocations.Where(l => l.Id != locationToSave.Id).ToList();
        var hasOtherMain = otherLocations.Any(l => l.IsMain);

        if (!locationToSave.IsMain && !hasOtherMain)
        {
            // Forzamos a que sea la principal, porque un negocio no puede quedarse sin sede central.
            locationToSave = locationToSave with { IsMain = true };
        }

        // Si el usuario marcó ESTA como principal, le quitamos el título a la que lo tuviera antes.
        if (locationToSave.IsMain)
        {
            foreach (var loc in otherLocations.Where(l => l.IsMain))
            {
                var updatedLoc = loc with { IsMain = false };
                await _locationRepository.SaveLocationAsync(request.WorkspaceId, updatedLoc, cancellationToken);
            }
        }

        await _locationRepository.SaveLocationAsync(request.WorkspaceId, locationToSave, cancellationToken);
        return Result.Success();
    }
}