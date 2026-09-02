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
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "Capacidad no soportada por el módulo SERVICES.", false, Array.Empty<string>());

        var activeServices = (await _serviceRepository.GetActiveServicesAsync(workspaceId, cancellationToken)).ToList();

        // 🔥 Auditoría (Sprint 3.1): Filtrado Multi-Sede inyectado desde el orquestador
        if (request.Parameters.TryGetValue("locationId", out var locObj) && locObj is string locationId && !string.IsNullOrWhiteSpace(locationId))
        {
            activeServices = activeServices.Where(s =>
                s.AvailableAtLocations == null ||
                !s.AvailableAtLocations.Any() ||
                s.AvailableAtLocations.Contains(locationId)).ToList();
        }

        // Si la IA identificó una categoría, filtramos adicionalmente en memoria
        if (request.Parameters.TryGetValue("category", out var categoryObj) && !string.IsNullOrWhiteSpace(categoryObj?.ToString()))
        {
            var categorySearch = categoryObj.ToString()!.ToLowerInvariant();
            var categoryFiltered = activeServices.Where(s => s.Category?.ToLowerInvariant() == categorySearch).ToList();

            if (categoryFiltered.Any())
                return BuildServicesResponse(categoryFiltered, request.CapabilityCode);
        }

        if (!activeServices.Any())
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, "Informa cortésmente que actualmente no hay servicios configurados o disponibles para la sede seleccionada.", false, Array.Empty<string>());

        // Mantenemos la lógica de agrupamiento para evitar sobrecargar la memoria de la IA
        if (activeServices.Count > 10)
        {
            var categories = activeServices
                .Select(s => string.IsNullOrWhiteSpace(s.Category) ? "Generales" : s.Category)
                .Distinct()
                .ToList();

            var categoriesText = string.Join(", ", categories);
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, $"El negocio ofrece {activeServices.Count} servicios distribuidos en estas categorías: {categoriesText}. Pregúntale al cliente qué tipo de servicio necesita para darle el detalle, duración y precio exacto.", false, Array.Empty<string>());
        }

        return BuildServicesResponse(activeServices, request.CapabilityCode);
    }

    private ModuleExecutionResult BuildServicesResponse(List<NexFlow.Application.Features.Business.ServiceDto> services, string capabilityCode)
    {
        var servicesText = string.Join("\n", services.Select(s =>
        {
            var durationText = s.DurationInMinutes > 0 ? $" ({s.DurationInMinutes} min)" : "";
            var reqReservation = s.RequiresReservation ? " [Requiere Reserva]" : "";
            var desc = !string.IsNullOrWhiteSpace(s.Description) ? $" - {s.Description}" : "";

            return $"- {s.Name}: {s.Currency} {s.PriceMinorUnits / 100m:0.00}{durationText}{reqReservation}{desc}";
        }));

        var responseText = $"Utiliza la siguiente lista de servicios y sus precios para responder la duda del cliente. NO ofrezcas servicios ni inventes precios que no estén en esta lista:\n{servicesText}";

        return new ModuleExecutionResult(true, ModuleCode, capabilityCode, responseText, false, Array.Empty<string>());
    }
}