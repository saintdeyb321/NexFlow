using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;

namespace NexFlow.Application.Features.Business.Locations;

public record SaveLocationCommand(Guid WorkspaceId, LocationDto Location, bool IsCreate = true);

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
        if (string.IsNullOrWhiteSpace(request.Location.Name) || string.IsNullOrWhiteSpace(request.Location.Address))
            return Result.Failure(new Error("Location.Invalid", "Nombre y dirección son obligatorios."));
        var maxLocations = request.IsCreate
            ? await _entitlementService.GetMaxLocationsAsync(request.WorkspaceId, cancellationToken) : int.MaxValue;
        return await _locationRepository.SaveLocationAsync(request.WorkspaceId,
            request.Location with { Name = request.Location.Name.Trim(), Address = request.Location.Address.Trim() },
            request.IsCreate, maxLocations, cancellationToken);
    }
}
