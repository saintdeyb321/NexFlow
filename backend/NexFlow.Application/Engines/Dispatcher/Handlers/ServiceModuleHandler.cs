using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class ServiceModuleHandler : IModuleHandler
{
    public string ModuleCode => "SERVICES";

    private readonly IServiceRepository _serviceRepository;

    public ServiceModuleHandler(IServiceRepository serviceRepository)
    {
        _serviceRepository = serviceRepository;
    }

    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode != "READ")
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { error = "Capacidad no soportada" }));

        var activeServices = (await _serviceRepository.GetActiveServicesAsync(workspaceId, cancellationToken)).ToList();

        bool isGlobalScope = request.Parameters.TryGetValue("locationScope", out var scopeObj) && scopeObj?.ToString() == "ALL";

        if (!isGlobalScope && request.Parameters.TryGetValue("locationId", out var locObj) && locObj is string locationId && !string.IsNullOrWhiteSpace(locationId))
        {
            activeServices = activeServices.Where(s =>
                s.AvailableAtLocations == null ||
                !s.AvailableAtLocations.Any() ||
                s.AvailableAtLocations.Contains(locationId)).ToList();
        }

        if (request.Parameters.TryGetValue("category", out var categoryObj) && !string.IsNullOrWhiteSpace(categoryObj?.ToString()))
        {
            var categorySearch = categoryObj.ToString()!.ToLowerInvariant();
            var categoryFiltered = activeServices.Where(s => s.Category?.ToLowerInvariant() == categorySearch).ToList();

            if (categoryFiltered.Any())
                return BuildServicesResponse(categoryFiltered, request.CapabilityCode);
        }

        if (!activeServices.Any())
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "empty", message = "No hay servicios disponibles" }));

        if (activeServices.Count > 10)
        {
            var categories = activeServices
                .Select(s => string.IsNullOrWhiteSpace(s.Category) ? "Generales" : s.Category)
                .Distinct()
                .ToList();

            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new
            {
                status = "too_many_results",
                totalCount = activeServices.Count,
                categories
            }));
        }

        return BuildServicesResponse(activeServices, request.CapabilityCode);
    }

    private ModuleExecutionResult BuildServicesResponse(List<NexFlow.Application.Features.Business.ServiceDto> services, string capabilityCode)
    {
        var resultData = services.Select(s => new
        {
            name = s.Name,
            price = $"{s.Currency} {s.PriceMinorUnits / 100m:0.00}",
            durationMin = s.DurationInMinutes > 0 ? s.DurationInMinutes : (int?)null,
            requiresReservation = s.RequiresReservation,
            description = s.Description
        });

        return new ModuleExecutionResult(true, ModuleCode, capabilityCode, JsonSerializer.Serialize(new { status = "success", services = resultData }));
    }
}