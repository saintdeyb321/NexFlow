using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class ReservationModuleHandler : IModuleHandler
{
    public string ModuleCode => "RESERVATIONS";

    private readonly ILocationRepository _locationRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly IReservationEngine _reservationEngine;
    private readonly IConversationCache _conversationCache;

    public ReservationModuleHandler(
        ILocationRepository locationRepository,
        IServiceRepository serviceRepository,
        IReservationEngine reservationEngine,
        IConversationCache conversationCache)
    {
        _locationRepository = locationRepository;
        _serviceRepository = serviceRepository;
        _reservationEngine = reservationEngine;
        _conversationCache = conversationCache;
    }
    public string[] SupportedCapabilities => new[] { "CHECK_AVAILABILITY", "CREATE", "CANCEL" };

    public async Task<string> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        var phone = request.Parameters.TryGetValue("phone", out var p) ? p?.ToString() ?? "unknown" : "unknown";
        var context = await _conversationCache.GetContextAsync(workspaceId, phone, cancellationToken) ?? new ConversationContextDto();

        // 1. RESOLVER SEDE
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        // 🔥 CORRECCIÓN: Usamos SelectedLocationId
        string? targetLocationId = context.SelectedLocationId;

        if (string.IsNullOrEmpty(targetLocationId))
        {
            if (locations.Count() == 1)
            {
                targetLocationId = locations.First().Id;
                context.SelectedLocationId = targetLocationId;
            }
            else if (locations.Any())
            {
                if (request.Parameters.TryGetValue("location", out var locName) && locName != null)
                {
                    var matchedLoc = locations.FirstOrDefault(l => l.Name.Contains(locName.ToString()!, StringComparison.OrdinalIgnoreCase));
                    if (matchedLoc != null) context.SelectedLocationId = matchedLoc.Id;
                }
                if (string.IsNullOrEmpty(context.SelectedLocationId))
                {
                    context.PendingAction = "ASK_LOCATION";
                    await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                    var locationNames = string.Join(", ", locations.Select(l => l.Name));
                    return $"SISTEMA: El negocio tiene varias sedes. Pregúntale amablemente al cliente en cuál desea reservar. Opciones: {locationNames}.";
                }
                targetLocationId = context.SelectedLocationId;
            }
            else
            {
                return "SISTEMA: El negocio aún no ha configurado sus sedes. Dile al cliente que por el momento no pueden agendar.";
            }
        }

        // 2. RESOLVER SERVICIO
        // 🔥 CORRECCIÓN: Usamos SelectedServiceId
        string? targetServiceId = context.SelectedServiceId;
        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);

        if (string.IsNullOrEmpty(targetServiceId))
        {
            if (request.Parameters.TryGetValue("service", out var serviceName) && serviceName != null)
            {
                var matchedService = services.FirstOrDefault(s => s.Name.Contains(serviceName.ToString()!, StringComparison.OrdinalIgnoreCase));
                if (matchedService != null) context.SelectedServiceId = matchedService.Id;
            }

            if (string.IsNullOrEmpty(context.SelectedServiceId))
            {
                context.PendingAction = "ASK_SERVICE";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                var serviceNames = string.Join(", ", services.Where(s => s.IsActive).Select(s => s.Name));
                return $"SISTEMA: Necesitamos saber qué servicio desea. Pregúntale al cliente qué servicio quiere agendar. Opciones: {serviceNames}.";
            }
            targetServiceId = context.SelectedServiceId;
        }

        if (string.IsNullOrEmpty(targetLocationId) || string.IsNullOrEmpty(targetServiceId))
        {
            return "SISTEMA: Error interno. No se pudo determinar la Sede o el Servicio a procesar.";
        }

        // 3. RESOLVER FECHA
        DateTime dateToSearch = DateTime.UtcNow.Date;
        if (request.Parameters.TryGetValue("date", out var dateStr) && dateStr != null && DateTime.TryParse(dateStr.ToString(), out var parsedDate))
        {
            dateToSearch = parsedDate.Date;
        }

        // --- RUTAS DE EJECUCIÓN FINAL ---
        if (request.CapabilityCode == "CHECK_AVAILABILITY")
        {
            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, targetLocationId!, targetServiceId!, dateToSearch, cancellationToken);
            context.PendingAction = "ASK_TIME";
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);

            if (!slots.Any())
                return $"SISTEMA: NO hay horarios disponibles el {dateToSearch:yyyy-MM-dd}. Pídele amablemente al cliente que elija otro día.";

            var slotsText = string.Join(", ", slots.Select(s => s.StartTime.ToString("HH:mm")));
            return $"SISTEMA: Horarios libres para el {dateToSearch:yyyy-MM-dd}: {slotsText}. Pregúntale al cliente cuál de estos horarios prefiere.";
        }

        if (request.CapabilityCode == "CREATE")
        {
            if (!request.Parameters.TryGetValue("time", out var timeStr) || timeStr == null || !TimeSpan.TryParse(timeStr.ToString(), out var time))
            {
                context.PendingAction = "ASK_TIME";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                return "SISTEMA: Falta la hora exacta. Pídele al cliente que indique a qué hora desea su cita.";
            }

            if (!request.Parameters.TryGetValue("name", out var customerName) || customerName == null || string.IsNullOrWhiteSpace(customerName.ToString()))
            {
                context.PendingAction = "ASK_NAME";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                return "SISTEMA: Falta el nombre. Dile que SÍ tienes disponibilidad a esa hora, pero necesitas su nombre completo para registrar la cita.";
            }

            var exactDateTime = dateToSearch.Add(time);
            string customerIdentifier = phone;

            var result = await _reservationEngine.CreateReservationAsync(workspaceId, targetLocationId!, targetServiceId!, customerIdentifier, customerName.ToString()!, exactDateTime, cancellationToken);

            if (result.IsSuccess)
            {
                // 🔥 CORRECCIÓN: Limpiamos los nuevos nombres de propiedades al tener éxito
                context.SelectedLocationId = null; context.SelectedServiceId = null; context.PendingAction = null; context.CurrentIntent = null;
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);

                return $"SISTEMA: Reserva CREADA EXITOSAMENTE a nombre de {customerName} para el {exactDateTime:yyyy-MM-dd HH:mm}. Confírmale al cliente con amabilidad y despídete.";
            }
            else
            {
                return $"SISTEMA: Hubo un conflicto. {result.Error.Description}. Pide disculpas y ofrécele otros horarios.";
            }
        }
        return "SISTEMA: La intención no es soportada por este módulo.";
    }
}