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
        // 0. RECUPERAR MEMORIA DE LA TRANSACCIÓN
        var phone = request.Parameters.TryGetValue("phone", out var p) ? p : "unknown";
        var context = await _conversationCache.GetContextAsync(workspaceId, phone, cancellationToken) ?? new ConversationContextDto();
        // 1. RESOLVER SEDE (Location)
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        string? targetLocationId = context.LocationId;

        if (string.IsNullOrEmpty(targetLocationId))
        {
            if (locations.Count() == 1)
            {
                targetLocationId = locations.First().Id;
                context.LocationId = targetLocationId; // Autocompletado si solo hay 1 sede
            }
            else if (locations.Any())
            {
                // Intentar extraer de lo que dijo el cliente ahora
                if (request.Parameters.TryGetValue("location", out var locName))
                {
                    var matchedLoc = locations.FirstOrDefault(l => l.Name.Contains(locName, StringComparison.OrdinalIgnoreCase));
                    if (matchedLoc != null) context.LocationId = matchedLoc.Id;
                }
                if (string.IsNullOrEmpty(context.LocationId))
                {
                    context.PendingAction = "ASK_LOCATION";
                    await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                    var locationNames = string.Join(", ", locations.Select(l => l.Name));
                    return $"SISTEMA: El negocio tiene varias sedes. Pregúntale amablemente al cliente en cuál desea reservar. Opciones: {locationNames}.";
                }
                targetLocationId = context.LocationId;
            }
            else
            {
                return "SISTEMA: El negocio aún no ha configurado sus sedes. Dile al cliente que por el momento no pueden agendar.";
            }
        }
        // 2. RESOLVER SERVICIO (Service)
        string? targetServiceId = context.ServiceId;
        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);

        if (string.IsNullOrEmpty(targetServiceId))
        {
            if (request.Parameters.TryGetValue("service", out var serviceName))
            {
                var matchedService = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
                if (matchedService != null) context.ServiceId = matchedService.Id;
            }

            if (string.IsNullOrEmpty(context.ServiceId))
            {
                context.PendingAction = "ASK_SERVICE";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                var serviceNames = string.Join(", ", services.Where(s => s.IsActive).Select(s => s.Name));
                return $"SISTEMA: Necesitamos saber qué servicio desea. Pregúntale al cliente qué servicio quiere agendar. Opciones: {serviceNames}.";
            }
            targetServiceId = context.ServiceId;
        }
        // 3. RESOLVER FECHA
        DateTime dateToSearch = DateTime.UtcNow.Date; // Por defecto hoy
        if (request.Parameters.TryGetValue("date", out var dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
        {
            dateToSearch = parsedDate.Date;
        }
        // --- RUTAS DE EJECUCIÓN FINAL ---
        if (request.CapabilityCode == "CHECK_AVAILABILITY")
        {
            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, targetLocationId, targetServiceId, dateToSearch, cancellationToken);
            context.PendingAction = "ASK_TIME";
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken); // Guardamos progreso

            if (!slots.Any())
                return $"SISTEMA: NO hay horarios disponibles el {dateToSearch:yyyy-MM-dd}. Pídele amablemente al cliente que elija otro día.";

            var slotsText = string.Join(", ", slots.Select(s => s.StartTime.ToString("HH:mm")));
            return $"SISTEMA: Horarios libres para el {dateToSearch:yyyy-MM-dd}: {slotsText}. Pregúntale al cliente cuál de estos horarios prefiere.";
        }
        if (request.CapabilityCode == "CREATE")
        {
            if (!request.Parameters.TryGetValue("time", out var timeStr) || !TimeSpan.TryParse(timeStr, out var time))
            {
                context.PendingAction = "ASK_TIME";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                return "SISTEMA: Falta la hora exacta. Pídele al cliente que indique a qué hora desea su cita.";
            }

            if (!request.Parameters.TryGetValue("name", out var customerName) || string.IsNullOrWhiteSpace(customerName))
            {
                context.PendingAction = "ASK_NAME";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                return "SISTEMA: Falta el nombre. Dile que SÍ tienes disponibilidad a esa hora, pero necesitas su nombre completo para registrar la cita.";
            }
            var exactDateTime = dateToSearch.Add(time);
            string customerIdentifier = phone;

            var result = await _reservationEngine.CreateReservationAsync(workspaceId, targetLocationId, targetServiceId, customerIdentifier, customerName, exactDateTime, cancellationToken);

            if (result.IsSuccess)
            {
                // ¡ÉXITO! Limpiamos la memoria porque la transacción terminó.
                context.LocationId = null; context.ServiceId = null; context.PendingAction = null; context.CurrentIntent = null;
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