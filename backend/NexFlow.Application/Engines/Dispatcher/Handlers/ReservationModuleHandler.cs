using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class ReservationModuleHandler : IModuleHandler
{
    public string ModuleCode => "RESERVATIONS";

    private readonly ILocationRepository _locationRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly IReservationEngine _reservationEngine;

    public ReservationModuleHandler(
        ILocationRepository locationRepository,
        IServiceRepository serviceRepository,
        IReservationEngine reservationEngine)
    {
        _locationRepository = locationRepository;
        _serviceRepository = serviceRepository;
        _reservationEngine = reservationEngine;
    }

    public bool CanHandle(IntentType intent) =>
        intent == IntentType.CheckAvailability || intent == IntentType.CreateReservation;

    public async Task<string> ExecuteCapabilityAsync(Guid workspaceId, IntentResultDto intent, CancellationToken cancellationToken)
    {
        // 1. Extraer y Validar Fecha (Fallback a hoy)
        DateTime dateToSearch = DateTime.UtcNow.Date;
        if (intent.Parameters.TryGetValue("date", out var dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
            dateToSearch = parsedDate.Date;

        // 2. Extraer y Validar Sede
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        var mainLocation = locations.FirstOrDefault(l => l.IsMain) ?? locations.FirstOrDefault();

        if (mainLocation == null || string.IsNullOrWhiteSpace(mainLocation.Id))
            return "SISTEMA: El negocio no tiene sedes configuradas. Dile al cliente que por el momento no pueden agendar.";

        string targetLocationId = mainLocation.Id;
        string? targetServiceId = null;

        // 3. Extraer y Validar Servicio
        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);

        if (intent.Parameters.TryGetValue("service", out var serviceName))
        {
            var matchedService = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
            if (matchedService != null)
            {
                targetServiceId = matchedService.Id;
            }
        }

        if (string.IsNullOrWhiteSpace(targetServiceId))
        {
            var serviceNames = string.Join(", ", services.Select(s => s.Name));
            return $"SISTEMA: Falta el servicio o no fue reconocido. Pide al cliente que elija uno de estos servicios: {serviceNames}";
        }

        // 4. ENRUTADOR DE CAPACIDADES (Capabilities Router)
        if (intent.Intent == IntentType.CheckAvailability)
        {
            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, targetLocationId, targetServiceId, dateToSearch, cancellationToken);

            if (!slots.Any())
                return $"SISTEMA: NO hay horarios disponibles el {dateToSearch:yyyy-MM-dd}. Pídele amablemente al cliente que elija otro día.";

            var slotsText = string.Join(", ", slots.Select(s => s.StartTime.ToString("HH:mm")));
            return $"SISTEMA: Horarios libres para {dateToSearch:yyyy-MM-dd}: {slotsText}. Pregúntale al cliente qué horario prefiere.";
        }

        if (intent.Intent == IntentType.CreateReservation)
        {
            // Validamos que la IA haya extraído la hora
            if (!intent.Parameters.TryGetValue("time", out var timeStr) || !TimeSpan.TryParse(timeStr, out var time))
                return "SISTEMA: Falta la hora exacta para la reserva. Pídele al cliente que indique a qué hora desea su cita.";

            var exactDateTime = dateToSearch.Add(time);

            // TODO: En el Sprint V2.11 inyectaremos el número de teléfono real del cliente vía Evolution API. 
            // Por ahora usamos un identificador seguro para que pase la creación.
            string customerIdentifier = intent.Parameters.TryGetValue("phone", out var phone) ? phone : "WhatsApp_Customer";

            var result = await _reservationEngine.CreateReservationAsync(workspaceId, targetLocationId, targetServiceId, customerIdentifier, exactDateTime, cancellationToken);

            if (result.IsSuccess)
                return $"SISTEMA: Reserva CREADA EXITOSAMENTE para el {exactDateTime:yyyy-MM-dd HH:mm}. Confírmale al cliente con amabilidad y despídete.";
            else
                return $"SISTEMA: Hubo un conflicto. {result.Error.Description}. Pide disculpas y ofrécele otros horarios.";
        }

        return "SISTEMA: La intención no es soportada por este módulo.";
    }
}