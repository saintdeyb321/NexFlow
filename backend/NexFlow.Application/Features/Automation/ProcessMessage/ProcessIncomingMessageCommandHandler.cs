using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

public class ProcessIncomingMessageCommandHandler
{
    private readonly IIntentEngine _intentEngine;
    private readonly IAiRouter _aiRouter;
    private readonly IMessageGateway _messageGateway;
    private readonly IEntitlementService _entitlementService;
    private readonly IReservationEngine _reservationEngine; // <-- Motor de reservas real
    private readonly IBusinessConfigurationRepository _businessConfigRepository; // <-- Datos de Firestore
    private readonly IConversationCache _conversationCache; // <-- Memoria de Redis
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;

    public ProcessIncomingMessageCommandHandler(
        IIntentEngine intentEngine,
        IAiRouter aiRouter,
        IMessageGateway messageGateway,
        IEntitlementService entitlementService,
        IReservationEngine reservationEngine,
        IBusinessConfigurationRepository businessConfigRepository,
        IConversationCache conversationCache,
        ILogger<ProcessIncomingMessageCommandHandler> logger)
    {
        _intentEngine = intentEngine;
        _aiRouter = aiRouter;
        _messageGateway = messageGateway;
        _entitlementService = entitlementService;
        _reservationEngine = reservationEngine;
        _businessConfigRepository = businessConfigRepository;
        _conversationCache = conversationCache;
        _logger = logger;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // 1. Seguridad: Validar Licencia
        if (!await _entitlementService.IsLicenseValidAsync(request.WorkspaceId, cancellationToken))
        {
            _logger.LogWarning("Workspace {Workspace} sin licencia activa.", request.WorkspaceId);
            return Result.Failure(new Error("License.Invalid", "Licencia inactiva."));
        }

        // 2. Extraer contexto anterior (Redis) para darle continuidad a la charla
        var lastIntent = await _conversationCache.GetLastIntentAsync(request.WorkspaceId, request.CustomerPhone, cancellationToken);

        // 3. IA: Clasificar Intención actual
        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);
        if (!intentResult.IsConfident()) intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        // 4. LÓGICA DE NEGOCIO REAL (Cero Mocks)
        string systemContext = string.Empty;

        switch (intentResult.Intent)
        {
            case IntentType.CheckAvailability:
                // Intentamos extraer la fecha solicitada, por defecto buscamos para hoy
                DateTime dateToSearch = DateTime.UtcNow.Date;
                if (intentResult.Parameters.TryGetValue("date", out var dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
                {
                    dateToSearch = parsedDate.Date;
                }

                // Buscamos la sede principal en Firestore para pasarle el ID al motor de reservas
                var locations = await _businessConfigRepository.GetLocationsAsync(request.WorkspaceId, cancellationToken);
                var mainLocation = locations.FirstOrDefault(l => l.IsMain) ?? locations.FirstOrDefault();

                if (mainLocation == null || !Guid.TryParse(mainLocation.Id, out var locationGuid))
                {
                    systemContext = "El sistema aún no tiene sedes configuradas. Pide disculpas amablemente.";
                    break;
                }

                // CONSULTA A POSTGRESQL + FIRESTORE: Disponibilidad estricta
                var slots = await _reservationEngine.GetAvailabilityAsync(
                    request.WorkspaceId,
                    locationGuid,
                    Guid.Empty, // (El ServiceId se resolverá en futuras mejoras)
                    dateToSearch,
                    cancellationToken);

                if (!slots.Any())
                {
                    systemContext = $"NO hay horarios disponibles para la fecha {dateToSearch:yyyy-MM-dd}. Ofrécele buscar en otro día.";
                }
                else
                {
                    var slotsText = string.Join(", ", slots.Select(s => s.StartTime.ToString("HH:mm")));
                    systemContext = $"Horarios libres REALES encontrados para {dateToSearch:yyyy-MM-dd}: {slotsText}. Muéstraselos al cliente amablemente.";
                }
                break;

            case IntentType.CreateReservation:
                systemContext = "El cliente quiere reservar. Verifica si ya tienes la fecha, hora y servicio. Si falta algo, pregúntaselo directamente.";
                break;

            default:
                // FAQ o Saludo: Traemos el perfil real de Firestore
                var profile = await _businessConfigRepository.GetProfileAsync(request.WorkspaceId, cancellationToken);
                systemContext = profile != null
                    ? $"Información oficial del negocio: Nombre: {profile.CommercialName}. Descripción/FAQ: {profile.Description}."
                    : "Contexto genérico. Sé amable y servicial.";
                break;
        }

        // 5. Guardar el nuevo estado en Redis
        await _conversationCache.SetLastIntentAsync(request.WorkspaceId, request.CustomerPhone, intentResult.Intent.ToString(), cancellationToken);

        // 6. IA: Redactar respuesta final con tono humano usando la data cruda inyectada
        var finalResponse = await _aiRouter.GenerateResponseAsync(request.WorkspaceId, intentResult, systemContext, cancellationToken);

        // 7. Gateway: Enviar WhatsApp
        await _messageGateway.SendTextAsync(request.WorkspaceId, request.CustomerPhone, finalResponse, cancellationToken);

        return Result.Success();
    }
}