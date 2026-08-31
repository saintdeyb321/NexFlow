using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class LocationModuleHandler : IModuleHandler
{
    private readonly ILocationRepository _locationRepo;

    public LocationModuleHandler(ILocationRepository locationRepo) => _locationRepo = locationRepo;

    public string ModuleCode => "LOCATIONS";
    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        var locations = await _locationRepo.GetLocationsAsync(workspaceId, cancellationToken);
        if (!locations.Any())
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "No hay sedes registradas.", false, Array.Empty<string>());

        var data = JsonSerializer.Serialize(locations);
        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, data, false, Array.Empty<string>());
    }
}