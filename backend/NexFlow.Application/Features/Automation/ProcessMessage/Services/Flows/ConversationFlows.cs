using NexFlow.Application.Features.Knowledge;
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
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Services.DTOs;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

public interface IBookingFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct); }
public interface IRequestFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, CancellationToken ct); }
public interface ISupportFlow { Task<string> ProcessAsync(Guid workspaceId, string conversationId, CancellationToken ct); }
public interface IChatFlow { Task<string> ProcessAsync(Guid workspaceId, string text, AiInterpretation interpretation, CancellationToken ct); }

public class BookingFlow : IBookingFlow
{
    private readonly IEntitlementService _entitlementService;
    private readonly IOfferingService _offeringService;
    private readonly ILocationResolverService _locationResolver;
    private readonly IReservationEngine _reservationEngine;
    private readonly ILocationRepository _locationRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly IMessageGateway _messageGateway;
    private readonly IContextRecoveryService _contextStore;

    public BookingFlow(
        IOfferingService offeringService, ILocationResolverService locationResolver,
        IReservationEngine reservationEngine, ILocationRepository locationRepo,
        IBusinessHoursRepository hoursRepo, IMessageGateway messageGateway, IEntitlementService entitlementService,
        IContextRecoveryService contextStore)
    {
        _offeringService = offeringService; _locationResolver = locationResolver;
        _reservationEngine = reservationEngine; _locationRepo = locationRepo;
        _hoursRepo = hoursRepo; _messageGateway = messageGateway;
        _contextStore = contextStore;
        _entitlementService = entitlementService;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct)
    {
        var response = await ProcessTurnAsync(workspaceId, phone, conversationId, context, interpretation, fallbackName, ct);
        await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
        return response;
    }

    private async Task<string> ProcessTurnAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct)
    {
        var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!activeModules.Contains("SERVICES")) return AiIntentAccess.ServicesUnavailable;
        if (!activeModules.Contains("RESERVATIONS")) return "No hay información de reservas disponible en este momento.";

        context.CurrentGoal = "RESERVATION";
        context.LastIntent = interpretation.Intent.ToString();

        bool requiresTimeReset = false;

        if (!string.IsNullOrWhiteSpace(interpretation.CustomerName)) context.RealCustomerName = interpretation.CustomerName;
        if (!string.IsNullOrWhiteSpace(interpretation.Location))
        {
            var realLocationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
            if (realLocationId == null)
            {
                context.SelectedLocationId = null; context.SelectedServiceId = null; context.TargetTime = null;
                context.MissingFields.Clear(); context.MissingFields.Add("Location");
                context.CurrentStep = "COLLECT_LOCATION";
                context.LastQuestion = "No pude identificar la sede. ¿Podrías indicar su nombre exacto y el servicio que deseas reservar?";
                return context.LastQuestion;
            }
            if (context.SelectedLocationId != realLocationId)
            {
                context.SelectedLocationId = realLocationId; requiresTimeReset = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(interpretation.Date) && context.TargetDate != interpretation.Date)
        {
            context.TargetDate = interpretation.Date; requiresTimeReset = true;
        }

        if (requiresTimeReset && string.IsNullOrWhiteSpace(interpretation.Time)) context.TargetTime = null;
        if (!string.IsNullOrWhiteSpace(interpretation.Time)) context.TargetTime = interpretation.Time;

        string? serviceClarification = null;
        if (!string.IsNullOrWhiteSpace(context.SelectedLocationId))
        {
            var workspaceLocations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
            if (!workspaceLocations.Any(l => l.Id == context.SelectedLocationId))
            {
                context.SelectedLocationId = null; context.SelectedServiceId = null; context.TargetTime = null;
            }
            else if (!string.IsNullOrWhiteSpace(interpretation.Service))
            {
                var resolution = await _offeringService.ResolveReservationServiceAsync(workspaceId, context.SelectedLocationId, interpretation.Service, ct);
                if (context.SelectedServiceId != resolution.Service?.Id && string.IsNullOrWhiteSpace(interpretation.Time))
                    context.TargetTime = null;
                context.SelectedServiceId = resolution.Service?.Id;
                serviceClarification = resolution.Clarification;
            }
            else if (!string.IsNullOrWhiteSpace(context.SelectedServiceId))
            {
                var selectedService = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId, ct);
                if (selectedService == null || !selectedService.RequiresReservation || !await _offeringService.IsServiceAvailableAtLocationAsync(workspaceId, context.SelectedServiceId, context.SelectedLocationId, ct))
                {
                    context.SelectedServiceId = null; context.TargetTime = null;
                    serviceClarification = "El servicio seleccionado ya no está disponible para reservar en esta sede. ¿Qué servicio deseas reservar?";
                }
            }
        }

        context.MissingFields.Clear();
        if (string.IsNullOrWhiteSpace(context.RealCustomerName)) context.MissingFields.Add("CustomerName");
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId)) context.MissingFields.Add("Location");
        if (string.IsNullOrWhiteSpace(context.SelectedServiceId)) context.MissingFields.Add("Service");
        if (string.IsNullOrWhiteSpace(context.TargetDate)) context.MissingFields.Add("Date");
        if (string.IsNullOrWhiteSpace(context.TargetTime)) context.MissingFields.Add("Time");

        if (!context.MissingFields.Any()) context.CurrentStep = "CONFIRM";
        else context.CurrentStep = $"COLLECT_{context.MissingFields.First().ToUpper()}";

        if (serviceClarification != null)
        {
            context.CurrentStep = "COLLECT_SERVICE";
            context.LastQuestion = serviceClarification;
            return serviceClarification;
        }

        switch (context.CurrentStep)
        {
            case "COLLECT_CUSTOMERNAME":
                context.LastQuestion = "¿me podrías indicar tu *nombre y apellido*?";
                return $"¡Excelente! Te ayudaré a agendar tu cita. 📅\n\nPara poder registrarte correctamente, {context.LastQuestion}";
            case "COLLECT_SERVICE":
                var availableServices = (await _offeringService.GetServicesAsync(workspaceId, context.SelectedLocationId, null, ct)).Where(s => s.RequiresReservation).ToList();
                if (!availableServices.Any()) return "Actualmente no contamos con servicios habilitados para reservas.";
                var serviceList = string.Join("\n", availableServices.Take(5).Select(s => $"- {s.Name}"));
                context.LastQuestion = "¿qué servicio deseas reservar?";
                return $"¡Gracias, {context.RealCustomerName}! \n\nAquí tienes algunos servicios solicitados:\n{serviceList}\n\n👉 *Por favor, {context.LastQuestion}*";
            case "COLLECT_LOCATION":
                var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
                if (!string.IsNullOrWhiteSpace(context.SelectedServiceId))
                {
                    var selectedService = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId, ct);
                    if (selectedService == null || !selectedService.RequiresReservation) return "El servicio seleccionado no está disponible para reservar.";
                    locations = locations.Where(l => string.Equals(selectedService.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase) || (l.Id != null && selectedService.LocationIds?.Contains(l.Id) == true)).ToList();
                }
                if (!locations.Any()) return "No hay sedes disponibles para el servicio seleccionado.";
                var locationList = string.Join("\n", locations.Select(l => $"- {l.Name}"));
                context.LastQuestion = "¿En cuál de nuestras sedes te gustaría atenderte y qué servicio deseas reservar?";
                return $"{context.LastQuestion}\n{locationList}";
            case "COLLECT_DATE":
                context.LastQuestion = "¿Para qué fecha te gustaría programar tu cita?";
                return $"¡Excelente! 🏥\n\n{context.LastQuestion}\n👉 *(Ej: 'mañana', o 'el 25 de octubre').*";
            case "COLLECT_TIME":
                if (!DateTime.TryParse(context.TargetDate, out var parsedDate)) { context.TargetDate = null; return "No logré entender la fecha. ¿Podrías decírmela en formato YYYY-MM-DD o 'mañana'?"; }
                var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, context.SelectedLocationId!, context.SelectedServiceId!, parsedDate, ct);
                if (!slots.Any()) { context.TargetDate = null; return $"Lo lamento mucho, tenemos la agenda llena el {parsedDate:dd/MM/yyyy}. ¿Intentamos otro día?"; }
                context.LastQuestion = "¿A qué hora prefieres que te agendemos?";
                return $"Tenemos turnos disponibles, {context.LastQuestion} (Ej: 'a las 10:00 am')";
            case "CONFIRM":
                var finalSrv = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId!, ct);
                if (finalSrv == null || !finalSrv.RequiresReservation || !await _offeringService.IsServiceAvailableAtLocationAsync(workspaceId, finalSrv.Id, context.SelectedLocationId!, ct))
                {
                    context.SelectedServiceId = null; context.TargetTime = null; context.MissingFields.Add("Service");
                    context.CurrentStep = "COLLECT_SERVICE"; context.LastQuestion = "El servicio ya no está disponible en esta sede. ¿Qué otro servicio deseas reservar?";
                    return context.LastQuestion;
                }
                var rawDateTime = $"{context.TargetDate} {context.TargetTime}";
                if (DateTime.TryParse(rawDateTime, out var exactDateTime))
                {
                    var result = await _reservationEngine.CreateReservationAsync(workspaceId, context.SelectedLocationId!, finalSrv!.Id, phone, context.RealCustomerName!, exactDateTime, ct);
                    if (result.IsSuccess)
                    {
                        context.CurrentGoal = null;
                        context.CurrentStep = null;
                        context.SelectedLocationId = null;
                        context.SelectedServiceId = null;
                        context.TargetDate = null;
                        context.TargetTime = null;
                        context.MissingFields.Clear();
                        context.LastQuestion = null;
                        return $"✅ *¡Todo listo, {context.RealCustomerName}!*\n\nTu cita ha sido confirmada exitosamente:\n🦷 Servicio: *{finalSrv.Name}*\n📅 Fecha: *{exactDateTime:dd/MM/yyyy}*\n⏰ Hora: *{exactDateTime:HH:mm}*\n\n¡Te esperamos!";
                    }
                    context.TargetTime = null; return $"Inconveniente: {result.Error.Description}. Indícame otro horario.";
                }
                context.TargetTime = null; return "Hubo un error al procesar el horario. ¿Podrías indicarme la hora nuevamente?";
            default: return "Estoy procesando tu solicitud...";
        }
    }
}

public class RequestFlow : IRequestFlow
{
    private readonly IRequestService _requestService;
    private readonly IHumanHandoffService _handoffService;

    public RequestFlow(IRequestService requestService, IHumanHandoffService handoffService)
    {
        _requestService = requestService; _handoffService = handoffService;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, CancellationToken ct)
    {
        var type = RequestType.Support;
        var title = "Solicitud de Atención";
        var lowerText = messageText.ToLowerInvariant();
        if (lowerText.Contains("comprar") || lowerText.Contains("cotizar") || lowerText.Contains("precio") || lowerText.Contains("mayor"))
        {
            type = RequestType.CommercialInquiry; title = "Consulta Comercial / Cotización";
        }
        else if (lowerText.Contains("tramite") || lowerText.Contains("afiliar") || lowerText.Contains("documento"))
        {
            type = RequestType.Tramite; title = "Trámite Administrativo";
        }
        var ticketId = await _requestService.CreateRequestAsync(workspaceId, phone, conversationId, type, title, messageText, null, null, ct);
        await _handoffService.EscalateToHumanAsync(workspaceId, conversationId, HandoffReason.AiEscalation, ct);
        return $"He registrado tu solicitud con el código {ticketId}. Un asesor la revisará y se pondrá en contacto contigo a la brevedad.";
    }
}

public class SupportFlow : ISupportFlow
{
    private readonly IHumanHandoffService _handoffService;
    public SupportFlow(IHumanHandoffService handoffService) => _handoffService = handoffService;

    public async Task<string> ProcessAsync(Guid workspaceId, string conversationId, CancellationToken ct)
    {
        await _handoffService.EscalateToHumanAsync(workspaceId, conversationId, HandoffReason.AiEscalation, ct);
        return "Entiendo. Te estoy transfiriendo con un asesor humano en este momento. Por favor espera un instante.";
    }
}

public class ChatFlow : IChatFlow
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly ILocationResolverService _locationResolver;
    private readonly ILogger<ChatFlow> _logger;
    private readonly IEntitlementService _entitlementService;

    public ChatFlow(IKnowledgeService knowledgeService, IEntitlementService entitlementService,
        ILocationResolverService locationResolver, ILogger<ChatFlow> logger)
    {
        _knowledgeService = knowledgeService;
        _locationResolver = locationResolver;
        _logger = logger;
        _entitlementService = entitlementService;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string text, AiInterpretation interpretation, CancellationToken ct)
    {
        var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var denied = interpretation.DeniedResponse ?? AiIntentAccess.GetDeniedResponse(interpretation.Intent, modules);
        if (denied != null) return denied;

        KnowledgeTopic? topic = interpretation.Intent switch
        {
            ConversationIntent.ProductQuery => KnowledgeTopic.Products,
            ConversationIntent.ServiceQuery => KnowledgeTopic.Services,
            ConversationIntent.Faq => KnowledgeTopic.Faqs,
            ConversationIntent.Location => KnowledgeTopic.Locations,
            ConversationIntent.BusinessHours => KnowledgeTopic.BusinessHours,
            ConversationIntent.General => KnowledgeTopic.Faqs,
            _ => null
        };
        if (!topic.HasValue) return "No puedo resolver esa consulta con la información disponible.";

        string? locationId = null;
        if (topic != KnowledgeTopic.Faqs && !string.IsNullOrWhiteSpace(interpretation.Location))
        {
            try
            {
                locationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
                if (string.IsNullOrWhiteSpace(locationId))
                    return new KnowledgeResult { Status = KnowledgeStatus.NotFound, Source = topic.Value }.ToResponse();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Knowledge location resolution failed for workspace {WorkspaceId}.", workspaceId);
                return new KnowledgeResult { Status = KnowledgeStatus.Unavailable, Source = topic.Value }.ToResponse();
            }
        }

        var snapshot = new BusinessKnowledgeSnapshot { WorkspaceId = workspaceId };
        var result = await _knowledgeService.QueryAsync(workspaceId, snapshot, new KnowledgeQuery
        {
            Topic = topic.Value,
            LocationId = locationId,
            SearchTerm = interpretation.Intent is ConversationIntent.Faq or ConversationIntent.General
                ? interpretation.SearchTerm ?? text : interpretation.SearchTerm
        }, ct);

        if (interpretation.Intent == ConversationIntent.General && result.Status == KnowledgeStatus.NotFound)
        {
            result = await _knowledgeService.QueryAsync(workspaceId, snapshot, new KnowledgeQuery { Topic = KnowledgeTopic.Profile }, ct);
            if (result.Found) return $"Esta es la información registrada del negocio:\n{result.Facts}\nSi necesitas otro dato, indícame cuál.";
        }

        // Business facts are returned verbatim from the factual query, never
        // expanded by an unconstrained generation step.
        return result.ToResponse();
    }
}