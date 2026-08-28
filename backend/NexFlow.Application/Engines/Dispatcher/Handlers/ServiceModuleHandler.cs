using NexFlow.Application.Abstractions;

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

    public async Task<string> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode != "READ")
            return "SISTEMA: Capacidad no soportada por el módulo SERVICES.";

        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);
        var activeServices = services.Where(s => s.IsActive).ToList();

        if (!activeServices.Any())
            return "SISTEMA: Informa cortésmente que actualmente no hay servicios configurados o disponibles en el catálogo.";

        // 🔥 SPRINT 3 (Auditoría #20): Límite de Contexto para servicios.
        if (activeServices.Count > 10)
        {
            var categories = activeServices
                .Select(s => string.IsNullOrWhiteSpace(s.Category) ? "Generales" : s.Category)
                .Distinct()
                .ToList();

            var categoriesText = string.Join(", ", categories);
            return $"SISTEMA: El negocio ofrece {activeServices.Count} servicios distribuidos en estas categorías: {categoriesText}. Pregúntale al cliente qué tipo de servicio necesita para darle el detalle, duración y precio exacto.";
        }

        var servicesText = string.Join("\n", activeServices.Select(s =>
        {
            var durationText = s.DurationInMinutes > 0 ? $" ({s.DurationInMinutes} min)" : "";
            var reqReservation = s.RequiresReservation ? " [Requiere Reserva]" : "";
            var desc = !string.IsNullOrWhiteSpace(s.Description) ? $" - {s.Description}" : "";

            return $"- {s.Name}: {s.Currency} {s.PriceMinorUnits / 100m}{durationText}{reqReservation}{desc}";
        }));

        return $@"SISTEMA: Utiliza la siguiente lista de servicios y sus precios para responder la duda del cliente. NO ofrezcas servicios que no estén en esta lista:
{servicesText}";
    }
}