using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class LocationModuleHandler : IModuleHandler
{
    private readonly ILocationRepository _locationRepo;

    public LocationModuleHandler(ILocationRepository locationRepo)
    {
        _locationRepo = locationRepo;
    }

    public string ModuleCode => "LOCATIONS";
    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        var locations = await _locationRepo.GetLocationsAsync(workspaceId, cancellationToken);

        if (!locations.Any())
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "empty", message = "No hay sedes registradas en este momento." }), false, Array.Empty<string>());

        // 🔥 SPRINT 9 y 10: Filtrado estricto sin propiedades inexistentes
        var resultData = locations.Select(l => new
        {
            name = l.Name,
            address = l.Address,
            mapsUrl = l.MapUrl,
            isMain = l.IsMain
        });

        var data = JsonSerializer.Serialize(new { status = "success", locations = resultData });

        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, data, false, Array.Empty<string>());
    }
}