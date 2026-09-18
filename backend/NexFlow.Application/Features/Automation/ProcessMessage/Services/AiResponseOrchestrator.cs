using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Requests;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using CacheContextDto = NexFlow.Application.Abstractions.Cache.ConversationContextDto;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IAiResponseOrchestrator
{
    Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken);
}

public sealed class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly IAiInterpreter _interpreter;
    private readonly IAiRouter _aiRouter;
    private readonly IEntitlementService _entitlementService;
    private readonly IOfferingService _offeringService;
    private readonly IReservationEngine _reservationEngine;
    private readonly ILocationResolverService _locationResolver;
    private readonly IRequestService _requestService;
    private readonly IHumanHandoffService _handoffService;
    private readonly IConversationRepository _conversationRepo;
    private readonly IConversationCache _cache;
    private readonly IMessageGateway _messageGateway;
    private readonly ILogger<AiResponseOrchestrator> _logger;

    public AiResponseOrchestrator(
        IAiInterpreter interpreter, IAiRouter aiRouter, IEntitlementService entitlementService,
        IOfferingService offeringService, IReservationEngine reservationEngine,
        ILocationResolverService locationResolver, IRequestService requestService, IHumanHandoffService handoffService,
        IConversationRepository conversationRepo, IConversationCache cache, IMessageGateway messageGateway,
        ILogger<AiResponseOrchestrator> logger)
    {
        _interpreter = interpreter; _aiRouter = aiRouter; _entitlementService = entitlementService;
        _offeringService = offeringService; _reservationEngine = reservationEngine;
        _locationResolver = locationResolver; _requestService = requestService; _handoffService = handoffService;
        _conversationRepo = conversationRepo; _cache = cache; _messageGateway = messageGateway;
        _logger = logger;
    }

    public async Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        var context = await _cache.GetContextAsync(workspaceId, normalizedPhone, cancellationToken) ?? new CacheContextDto();
        var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var interpretation = await _interpreter.InterpretAsync(request.MessageText, context.CurrentGoal ?? "", activeModules, cancellationToken);

        string finalResponse;

        if ((interpretation.Intent == "BOOKING" || context.CurrentGoal == "BOOKING") && activeModules.Contains("RESERVATIONS"))
        {
            context.CurrentGoal = "BOOKING";
            if (!string.IsNullOrWhiteSpace(interpretation.Service)) context.SelectedServiceId = interpretation.Service;
            if (!string.IsNullOrWhiteSpace(interpretation.Date)) context.TargetDate = interpretation.Date;
            if (!string.IsNullOrWhiteSpace(interpretation.Time)) context.TargetTime = interpretation.Time;

            if (!string.IsNullOrWhiteSpace(interpretation.Location))
            {
                var realLocationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, cancellationToken);
                if (realLocationId != null) context.SelectedLocationId = realLocationId;
            }

            finalResponse = await HandleBookingFlowAsync(workspaceId, normalizedPhone, context, request.CustomerName, cancellationToken);
        }
        else if (interpretation.Intent == "REQUEST" && activeModules.Contains("REQUESTS"))
        {
            var ticketId = await _requestService.CreateSupportTicketAsync(workspaceId, normalizedPhone, request.MessageText, cancellationToken);
            await _handoffService.EscalateToHumanAsync(workspaceId, conversation.Id, HandoffReason.AiEscalation, cancellationToken);
            finalResponse = $"He registrado tu solicitud con el código {ticketId}. Un asesor se pondrá en contacto contigo a la brevedad.";
        }
        else if (interpretation.Intent == "SUPPORT")
        {
            await _handoffService.EscalateToHumanAsync(workspaceId, conversation.Id, HandoffReason.AiEscalation, cancellationToken);
            finalResponse = "Entiendo. Te estoy transfiriendo con un asesor humano en este momento. Por favor espera un instante.";
        }
        else
        {
            // Para CHAT complejo o si intentó usar un módulo no pagado, usamos Gemini
            finalResponse = await GenerarRespuestaAmigableAsync(request.MessageText, cancellationToken);
        }

        await _cache.SetContextAsync(workspaceId, normalizedPhone, context, cancellationToken);
        await PersistAndSendAsync(workspaceId, normalizedPhone, conversation.Id, finalResponse, cancellationToken);
    }

    private async Task<string> HandleBookingFlowAsync(Guid workspaceId, string phone, CacheContextDto context, string customerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.SelectedServiceId)) return "¡Claro! Te ayudaré a reservar. ¿Qué servicio te gustaría programar?";
        var offerings = await _offeringService.SearchOfferingsAsync(workspaceId, null, "SERVICE", context.SelectedServiceId, ct);
        var targetService = offerings.FirstOrDefault();
        if (targetService == null) return $"Disculpa, no encontré el servicio '{context.SelectedServiceId}'. ¿Podrías confirmarme el nombre?";
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId)) return $"Perfecto, has elegido {targetService.Name}. ¿En cuál de nuestras sedes te gustaría atenderte?";

        bool isAvailable = await _offeringService.IsAvailableAtLocationAsync(workspaceId, targetService.Id, context.SelectedLocationId, ct);
        if (!isAvailable) return $"El servicio de {targetService.Name} no está disponible en esa sede. ¿Deseas ver otra sede?";
        if (string.IsNullOrWhiteSpace(context.TargetDate)) return $"Excelente. ¿Para qué fecha te gustaría programar tu cita de {targetService.Name}?";

        if (!DateTime.TryParse(context.TargetDate, out var date)) return "No logré entender la fecha. ¿Podrías decírmela en formato YYYY-MM-DD o 'mañana'?";

        if (string.IsNullOrWhiteSpace(context.TargetTime))
        {
            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, context.SelectedLocationId, targetService.Id, date, ct);
            if (!slots.Any()) return $"Lo lamento mucho, pero no tenemos horarios libres el {date:dd/MM/yyyy}. ¿Te gustaría intentar con otra fecha?";
            var horasLibres = string.Join(", ", slots.Take(5).Select(s => s.StartTime.ToString("HH:mm")));
            return $"Para el {date:dd/MM/yyyy} tenemos estos horarios libres: {horasLibres}. ¿Cuál prefieres?";
        }

        var rawDateTime = $"{context.TargetDate} {context.TargetTime}";
        if (DateTime.TryParse(rawDateTime, out var exactDateTime))
        {
            var result = await _reservationEngine.CreateReservationAsync(workspaceId, context.SelectedLocationId, targetService.Id, phone, customerName, exactDateTime, ct);
            if (result.IsSuccess)
            {
                context.CurrentGoal = null;
                return $"¡Listo! Tu reserva para {targetService.Name} el {exactDateTime:dd/MM/yyyy} a las {exactDateTime:HH:mm} ha sido confirmada con éxito. ¡Te esperamos!";
            }
            context.TargetTime = null;
            return $"Tuvimos un inconveniente: {result.Error.Description}. Por favor, elige otro horario.";
        }
        return "Hubo un error al procesar el horario. ¿Podrías indicarme la hora nuevamente?";
    }

    private async Task<string> GenerarRespuestaAmigableAsync(string text, CancellationToken ct)
    {
        var provider = _aiRouter.GetProvider(AiTaskType.ComplexChat);
        return await provider.GenerateTextAsync("Eres un asistente amable. Responde con cortesía y guía al cliente.", text, false, ct);
    }

    private async Task PersistAndSendAsync(Guid workspaceId, string phone, string conversationId, string text, CancellationToken ct)
    {
        var pendingId = Guid.NewGuid().ToString();
        await _conversationRepo.AddMessageAsync(workspaceId, conversationId, new MessageRecord { Id = pendingId, ExternalMessageId = pendingId, Direction = "outbound", Sender = SenderType.AI, Content = text, Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow }, ct);
        try
        {
            var extId = await _messageGateway.SendTextAsync(workspaceId, phone, text, ct);
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Sent, extId, ct);
        }
        catch
        {
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Failed, null, ct);
        }
    }
}