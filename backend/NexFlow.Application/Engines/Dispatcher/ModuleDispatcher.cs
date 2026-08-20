using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Engines.Dispatcher;

public class ModuleDispatcher : IModuleDispatcher
{
    private readonly IBusinessConfigurationRepository _businessConfigRepository;
    private readonly IReservationEngine _reservationEngine;

    public ModuleDispatcher(
        IBusinessConfigurationRepository businessConfigRepository,
        IReservationEngine reservationEngine)
    {
        _businessConfigRepository = businessConfigRepository;
        _reservationEngine = reservationEngine;
    }

    public async Task<string> BuildSystemContextAsync(Guid workspaceId, IntentResultDto intentResult, CancellationToken cancellationToken)
    {
        switch (intentResult.Intent)
        {
            case IntentType.CheckAvailability:
            case IntentType.CreateReservation:
                return await HandleReservationIntentAsync(workspaceId, intentResult, cancellationToken);

            case IntentType.Faq:
            default:
                return await HandleKnowledgeIntentAsync(workspaceId, cancellationToken);
        }
    }

    private async Task<string> HandleReservationIntentAsync(Guid workspaceId, IntentResultDto intent, CancellationToken cancellationToken)
    {
        // 1. Identificar la fecha
        DateTime dateToSearch = DateTime.UtcNow.Date;
        if (intent.Parameters.TryGetValue("date", out var dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
        {
            dateToSearch = parsedDate.Date;
        }

        // 2. Resolver la Sede
        var locations = await _businessConfigRepository.GetLocationsAsync(workspaceId, cancellationToken);
        var mainLocation = locations.FirstOrDefault(l => l.IsMain) ?? locations.FirstOrDefault();
        if (mainLocation == null || !Guid.TryParse(mainLocation.Id, out var locationGuid))
            return "El sistema aún no tiene sedes configuradas. Pide disculpas.";

        // 3. Resolver el Servicio REAL (Aniquilamos el Guid.Empty)
        Guid targetServiceId = Guid.Empty;
        var services = await _businessConfigRepository.GetServicesAsync(workspaceId, cancellationToken);

        if (intent.Parameters.TryGetValue("service", out var serviceName))
        {
            var matchedService = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
            if (matchedService != null) targetServiceId = matchedService.Id;
        }

        if (targetServiceId == Guid.Empty)
        {
            var serviceNames = string.Join(", ", services.Select(s => s.Name));
            return $"No detectamos un servicio exacto. Pregúntale al cliente cuál de estos servicios desea: {serviceNames}";
        }

        // 4. Consultar Disponibilidad Exacta
        var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, locationGuid, targetServiceId, dateToSearch, cancellationToken);

        if (!slots.Any()) return $"NO hay horarios disponibles el {dateToSearch:yyyy-MM-dd} para ese servicio. Ofrécele otro día.";

        var slotsText = string.Join(", ", slots.Select(s => s.StartTime.ToString("HH:mm")));
        return $"Horarios libres REALES encontrados para {dateToSearch:yyyy-MM-dd}: {slotsText}. Muéstraselos.";
    }

    private async Task<string> HandleKnowledgeIntentAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var profile = await _businessConfigRepository.GetProfileAsync(workspaceId, cancellationToken);
        var faqs = await _businessConfigRepository.GetFaqsAsync(workspaceId, cancellationToken);

        var faqsText = string.Join(" | ", faqs.Select(f => $"P: {f.Question} R: {f.Answer}"));

        return profile != null
            ? $"Nombre negocio: {profile.CommercialName}. Descripción: {profile.Description}. FAQs: {faqsText}"
            : "Contexto genérico. Sé amable.";
    }
}