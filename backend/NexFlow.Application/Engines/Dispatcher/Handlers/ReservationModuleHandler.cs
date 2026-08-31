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

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        var phone = request.Parameters.TryGetValue("phone", out var p) ? p?.ToString() ?? "unknown" : "unknown";
        var context = await _conversationCache.GetContextAsync(workspaceId, phone, cancellationToken) ?? new ConversationContextDto();

        if (request.CapabilityCode == "CANCEL")
        {
            context.SelectedLocationId = null; context.SelectedServiceId = null; context.PendingAction = null; context.CurrentIntent = null;
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);

            // Activamos el flag booleano RequiresHuman a true en el objeto estructurado
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, "Indícale al cliente que has recibido su solicitud de cancelación y que un asesor se comunicará en breve para procesarla.", true, Array.Empty<string>());
        }

        // 1. RESOLVER SEDE
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
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
                    return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, $"El negocio tiene varias sedes. Pregúntale amablemente al cliente en cuál desea reservar. Opciones: {locationNames}.", false, new[] { "locationId" });
                }
                targetLocationId = context.SelectedLocationId;
            }
            else
            {
                return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "El negocio aún no ha configurado sus sedes. Dile al cliente que por el momento no pueden agendar.", false, Array.Empty<string>());
            }
        }

        // 2. RESOLVER SERVICIO
        string? targetServiceId = context.SelectedServiceId;
        var allServices = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);
        var services = allServices.Where(s => s.IsActive).ToList();

        if (string.IsNullOrEmpty(targetServiceId))
        {
            if (request.Parameters.TryGetValue("service", out var serviceName) && serviceName != null)
            {
                var searchStr = serviceName.ToString()!;
                var exactMatch = services.FirstOrDefault(s => s.Name.Equals(searchStr, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    context.SelectedServiceId = exactMatch.Id;
                }
                else
                {
                    var matchedServices = services.Where(s => s.Name.Contains(searchStr, StringComparison.OrdinalIgnoreCase)).ToList();

                    if (matchedServices.Count == 1)
                    {
                        context.SelectedServiceId = matchedServices.First().Id;
                    }
                    else if (matchedServices.Count > 1)
                    {
                        context.PendingAction = "ASK_SERVICE";
                        await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                        var options = string.Join(", ", matchedServices.Select(m => m.Name));
                        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, $"Hay varios servicios que coinciden con '{searchStr}'. Opciones: {options}. Pregunta cuál de estos específicos desea.", false, new[] { "serviceId" });
                    }
                }
            }

            if (string.IsNullOrEmpty(context.SelectedServiceId))
            {
                context.PendingAction = "ASK_SERVICE";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                var serviceNames = string.Join(", ", services.Select(s => s.Name));
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, $"Necesitamos saber qué servicio desea. Pregúntale al cliente qué servicio quiere agendar. Opciones: {serviceNames}.", false, new[] { "serviceId" });
            }
            targetServiceId = context.SelectedServiceId;
        }

        if (string.IsNullOrEmpty(targetLocationId) || string.IsNullOrEmpty(targetServiceId))
        {
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "Error interno. No se pudo determinar la Sede o el Servicio a procesar.", false, Array.Empty<string>());
        }

        // 3. RESOLVER FECHA
        DateTime dateToSearch = DateTime.UtcNow.Date;
        if (request.Parameters.TryGetValue("date", out var dateStr) && dateStr != null && DateTime.TryParse(dateStr.ToString(), out var parsedDate))
        {
            dateToSearch = parsedDate.Date;
        }

        if (request.CapabilityCode == "CHECK_AVAILABILITY")
        {
            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, targetLocationId!, targetServiceId!, dateToSearch, cancellationToken);
            context.PendingAction = "ASK_TIME";
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);

            if (!slots.Any())
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, $"NO hay horarios disponibles el {dateToSearch:yyyy-MM-dd}. Pídele amablemente al cliente que elija otro día.", false, Array.Empty<string>());

            var slotsText = string.Join(", ", slots.Select(s => s.StartTime.ToString("HH:mm")));
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, $"Horarios libres para el {dateToSearch:yyyy-MM-dd}: {slotsText}. Pregúntale al cliente cuál de estos horarios prefiere.", false, new[] { "time" });
        }

        if (request.CapabilityCode == "CREATE")
        {
            if (!request.Parameters.TryGetValue("time", out var timeStr) || timeStr == null || !TimeSpan.TryParse(timeStr.ToString(), out var time))
            {
                context.PendingAction = "ASK_TIME";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, "Falta la hora exacta. Pídele al cliente que indique a qué hora desea su cita.", false, new[] { "time" });
            }

            if (!request.Parameters.TryGetValue("name", out var customerName) || customerName == null || string.IsNullOrWhiteSpace(customerName.ToString()))
            {
                context.PendingAction = "ASK_NAME";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, "Falta el nombre. Dile que SÍ tienes disponibilidad a esa hora, pero necesitas su nombre completo para registrar la cita.", false, new[] { "name" });
            }

            var exactDateTime = dateToSearch.Add(time);
            string customerIdentifier = phone;

            var result = await _reservationEngine.CreateReservationAsync(workspaceId, targetLocationId!, targetServiceId!, customerIdentifier, customerName.ToString()!, exactDateTime, cancellationToken);

            if (result.IsSuccess)
            {
                context.SelectedLocationId = null; context.SelectedServiceId = null; context.PendingAction = null; context.CurrentIntent = null;
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);

                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, $"Reserva CREADA EXITOSAMENTE a nombre de {customerName} para el {exactDateTime:yyyy-MM-dd HH:mm}. Confírmale al cliente con amabilidad y despídete.", false, Array.Empty<string>());
            }
            else
            {
                return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, $"Hubo un conflicto. {result.Error.Description}. Pide disculpas y ofrécele otros horarios.", false, Array.Empty<string>());
            }
        }

        return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "La intención no es soportada por este módulo.", false, Array.Empty<string>());
    }
}