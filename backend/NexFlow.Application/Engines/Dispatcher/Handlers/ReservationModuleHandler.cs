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
        DateTime dateToSearch = DateTime.UtcNow.Date;
        if (intent.Parameters.TryGetValue("date", out var dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
            dateToSearch = parsedDate.Date;

        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        var mainLocation = locations.FirstOrDefault(l => l.IsMain) ?? locations.FirstOrDefault();
        if (mainLocation == null || !Guid.TryParse(mainLocation.Id, out var locationGuid))
            return "El sistema aún no tiene sedes configuradas.";

        Guid targetServiceId = Guid.Empty;
        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);

        if (intent.Parameters.TryGetValue("service", out var serviceName))
        {
            var matchedService = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
            if (matchedService != null) Guid.TryParse(matchedService.Id, out targetServiceId);
        }

        if (targetServiceId == Guid.Empty)
        {
            var serviceNames = string.Join(", ", services.Select(s => s.Name));
            return $"Pregunta al cliente cuál de estos servicios desea: {serviceNames}";
        }

        var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, locationGuid, targetServiceId, dateToSearch, cancellationToken);

        if (!slots.Any()) return $"NO hay horarios disponibles el {dateToSearch:yyyy-MM-dd}.";

        var slotsText = string.Join(", ", slots.Select(s => s.StartTime.ToString("HH:mm")));
        return $"Horarios libres para {dateToSearch:yyyy-MM-dd}: {slotsText}.";
    }
}