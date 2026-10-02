using System.Globalization;
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
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Notifications;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

public interface IBookingFlow { Task<string?> TryResumeAsync(Guid workspaceId, string phone, string sourceMessageId, ConversationContextDto context, CancellationToken ct); Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, string sourceMessageId, CancellationToken ct); Task<string> GetContinuationAsync(Guid workspaceId, ConversationContextDto context, CancellationToken ct, bool concise = false); }
public interface IRequestFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, string sourceMessageId, CancellationToken ct); }
public interface ISupportFlow { Task<string> ProcessAsync(Guid workspaceId, string conversationId, CancellationToken ct); }
public record ChatResponse(string Text, string? MediaUrl = null, string? FileName = null, string? CatalogScope = null);
public interface IChatFlow { Task<ChatResponse> ProcessAsync(Guid workspaceId, string text, AiInterpretation interpretation, string? factualLocationId, CancellationToken ct); }

public class BookingFlow : IBookingFlow
{
    private readonly IEntitlementService _entitlementService;
    private readonly IOfferingService _offeringService;
    private readonly ILocationResolverService _locationResolver;
    private readonly IReservationEngine _reservationEngine;
    private readonly IReservationRepository _reservationRepository;
    private readonly ILocationRepository _locationRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IContextRecoveryService _contextStore;

    public BookingFlow(
        IOfferingService offeringService, ILocationResolverService locationResolver,
        IReservationEngine reservationEngine, ILocationRepository locationRepo,
        IBusinessHoursRepository hoursRepo, IEntitlementService entitlementService,
        IContextRecoveryService contextStore, IReservationRepository reservationRepository,
        IBusinessProfileRepository profileRepo, IKnowledgeService knowledgeService)
    {
        _offeringService = offeringService; _locationResolver = locationResolver;
        _reservationEngine = reservationEngine; _locationRepo = locationRepo;
        _hoursRepo = hoursRepo; _profileRepo = profileRepo; _knowledgeService = knowledgeService;
        _contextStore = contextStore; _reservationRepository = reservationRepository;
        _entitlementService = entitlementService;
    }

    public async Task<string?> TryResumeAsync(Guid workspaceId, string phone, string sourceMessageId, ConversationContextDto context, CancellationToken ct)
    {
        var existing = await _reservationRepository.GetBySourceMessageIdAsync(workspaceId, sourceMessageId, ct);
        if (existing == null) return null;
        if (existing.CustomerIdentifier != phone) throw new InvalidOperationException("Reservation inbound recipient mismatch.");
        if (context.CurrentGoal is "BOOKING" or "RESERVATION")
        {
            ClearDraft(context);
            await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
        }
        return $"✅ Tu reserva ya está registrada con el código {existing.Id}.";
    }

    private static void ClearDraft(ConversationContextDto context)
    {
        context.CurrentGoal = null; context.CurrentStep = null;
        context.SelectedLocationId = null; context.SelectedServiceId = null;
        context.ReservationServiceOffset = 0;
        context.TargetDate = null; context.TargetTime = null;
        context.MissingFields.Clear(); context.LastQuestion = null;
    }

    private static void UpdateStep(ConversationContextDto context)
    {
        context.MissingFields.Clear();
        if (string.IsNullOrWhiteSpace(context.RealCustomerName)) context.MissingFields.Add("CustomerName");
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId)) context.MissingFields.Add("Location");
        if (string.IsNullOrWhiteSpace(context.SelectedServiceId)) context.MissingFields.Add("Service");
        if (string.IsNullOrWhiteSpace(context.TargetDate)) context.MissingFields.Add("Date");
        if (string.IsNullOrWhiteSpace(context.TargetTime)) context.MissingFields.Add("Time");
        context.CurrentStep = context.MissingFields.Count == 0 ? "CONFIRM"
            : $"COLLECT_{context.MissingFields[0].ToUpperInvariant()}";
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, string sourceMessageId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMessageId);
        var replay = await TryResumeAsync(workspaceId, phone, sourceMessageId, context, ct);
        if (replay != null) return replay;
        var response = await ProcessTurnAsync(workspaceId, phone, context, interpretation, sourceMessageId, ct);
        await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
        return response;
    }

    private async Task<string> ProcessTurnAsync(Guid workspaceId, string phone, ConversationContextDto context, AiInterpretation interpretation, string sourceMessageId, CancellationToken ct)
    {
        var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!modules.Contains("SERVICES")) return AiIntentAccess.ServicesUnavailable;
        if (!modules.Contains("RESERVATIONS")) return "No hay información de reservas disponible en este momento.";

        // Informational turns are handled before applying extracted entities.
        // Neither a question's location nor its date/time can overwrite the draft.
        if (context.CurrentGoal is "BOOKING" or "RESERVATION")
        {
            if (interpretation.Directive == ConversationDirective.CancelCurrentFlow)
            {
                ClearDraft(context);
                return "He cancelado el borrador de tu reserva. ¿En qué más puedo ayudarte?";
            }
            if (interpretation.Directive == ConversationDirective.ChangeLocation)
            {
                context.SelectedLocationId = null; context.SelectedServiceId = null; context.TargetTime = null;
                context.ReservationServiceOffset = 0;
                UpdateStep(context);
                context.CurrentStep = "COLLECT_LOCATION";
                return await LocationOptionsAsync(workspaceId, "¿A cuál sede deseas cambiar?", ct);
            }
            if (interpretation.Directive == ConversationDirective.ChangeService)
            {
                context.SelectedServiceId = null; context.TargetTime = null;
                UpdateStep(context);
                if (string.IsNullOrWhiteSpace(interpretation.Service))
                    return await GetContinuationAsync(workspaceId, context, ct);
            }
            else if (interpretation.Directive == ConversationDirective.ShowOptions
                || (interpretation.Intent == ConversationIntent.ServiceQuery && interpretation.QueryKind == OfferingQueryKind.Broad))
            {
                if (string.IsNullOrWhiteSpace(context.SelectedLocationId))
                    return await LocationOptionsAsync(workspaceId, "¿En cuál sede deseas consultar los servicios reservables?", ct);
                var services = await ReservableServicesAsync(workspaceId, context.SelectedLocationId, ct);
                var nextOffset = context.ReservationServiceOffset + 5;
                context.ReservationServiceOffset = services.Count > nextOffset ? nextOffset : 0;
                var options = OfferingServiceReservationExtensions.FormatReservationOptions(services, context.ReservationServiceOffset);
                if (context.CurrentStep == "COLLECT_SERVICE") return options;
                // Keep the current step and every collected entity.
                return RemoveFinalQuestion(options) + "\n\n" + await GetContinuationAsync(workspaceId, context, ct);
            }
            else if (interpretation.Intent is ConversationIntent.BusinessHours or ConversationIntent.Location or ConversationIntent.Faq)
            {
                string answer;
                if (interpretation.Intent == ConversationIntent.BusinessHours)
                {
                    if (string.IsNullOrWhiteSpace(context.SelectedLocationId))
                    {
                        var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
                        answer = "Los horarios dependen de la sede. Primero debes elegir una de estas sedes:\n" +
                            string.Join("\n", locations.Select(l => $"• {l.Name}"));
                    }
                    else answer = await WeeklyHoursAsync(workspaceId, context.SelectedLocationId, ct);
                }
                else
                {
                    var factualLocationId = context.SelectedLocationId;
                    if (interpretation.Intent == ConversationIntent.Location && !string.IsNullOrWhiteSpace(interpretation.Location))
                        factualLocationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
                    var knowledgeResult = await _knowledgeService.QueryAsync(workspaceId, new BusinessKnowledgeSnapshot { WorkspaceId = workspaceId },
                        new KnowledgeQuery { Topic = interpretation.Intent == ConversationIntent.Location ? KnowledgeTopic.Locations : KnowledgeTopic.Faqs,
                            LocationId = interpretation.Intent == ConversationIntent.Location ? factualLocationId : null,
                            SearchTerm = interpretation.SearchTerm }, ct);
                    answer = knowledgeResult.Status == KnowledgeStatus.NotFound ? "No tengo esa información registrada." : knowledgeResult.ToResponse();
                }
                return answer + "\n\nPara continuar con tu reserva:\n" + await GetContinuationAsync(workspaceId, context, ct);
            }
        }

        context.CurrentGoal = "RESERVATION";
        context.LastIntent = interpretation.Intent.ToString();
        if (!string.IsNullOrWhiteSpace(interpretation.CustomerName)) context.RealCustomerName = interpretation.CustomerName;
        if (!string.IsNullOrWhiteSpace(interpretation.Location))
        {
            var locationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
            if (locationId == null)
            {
                // An unrecognized answer does not discard an existing valid selection.
                UpdateStep(context);
                return "No pude identificar esa sede.\n\n" + await LocationOptionsAsync(workspaceId, "¿Cuál sede deseas elegir?", ct);
            }
            if (context.SelectedLocationId != locationId)
            {
                context.SelectedLocationId = locationId; context.SelectedServiceId = null; context.TargetTime = null;
                context.ReservationServiceOffset = 0;
            }
        }
        if (!string.IsNullOrWhiteSpace(interpretation.Date) && context.TargetDate != interpretation.Date)
        {
            context.TargetDate = interpretation.Date; context.TargetTime = null;
        }
        if (!string.IsNullOrWhiteSpace(interpretation.Time)) context.TargetTime = interpretation.Time;

        if (!string.IsNullOrWhiteSpace(context.SelectedLocationId))
        {
            var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
            if (!locations.Any(l => l.Id == context.SelectedLocationId))
            {
                context.SelectedLocationId = null; context.SelectedServiceId = null; context.TargetTime = null;
            }
            else if (!string.IsNullOrWhiteSpace(interpretation.Service))
            {
                var resolution = await _offeringService.ResolveReservationServiceAsync(workspaceId, context.SelectedLocationId, interpretation.Service, ct);
                if (resolution.Service == null)
                {
                    UpdateStep(context);
                    return resolution.Clarification!;
                }
                if (context.SelectedServiceId != resolution.Service.Id && string.IsNullOrWhiteSpace(interpretation.Time))
                    context.TargetTime = null;
                context.SelectedServiceId = resolution.Service.Id;
            }
            else if (!string.IsNullOrWhiteSpace(context.SelectedServiceId) && !await IsSelectedServiceValidAsync(workspaceId, context, ct))
            {
                context.SelectedServiceId = null; context.TargetTime = null;
                UpdateStep(context);
                return "El servicio seleccionado ya no está disponible para reservar en esta sede.\n\n" +
                    await ServiceOptionsAsync(workspaceId, context.SelectedLocationId, ct);
            }
        }
        UpdateStep(context);
        if (context.CurrentStep != "CONFIRM") return await PromptAsync(workspaceId, context, false, ct);

        var service = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId!, ct);
        if (service == null || !await IsSelectedServiceValidAsync(workspaceId, context, ct))
        {
            context.SelectedServiceId = null; context.TargetTime = null;
            UpdateStep(context);
            return "El servicio ya no está disponible en esta sede.\n\n" + await ServiceOptionsAsync(workspaceId, context.SelectedLocationId!, ct);
        }
        if (!DateTime.TryParseExact($"{context.TargetDate} {context.TargetTime}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selectedTime))
        {
            context.TargetTime = null;
            if (!DateTime.TryParseExact(context.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                context.TargetDate = null;
            UpdateStep(context);
            return "No pude interpretar la fecha u hora indicada.\n\n" + await PromptAsync(workspaceId, context, false, ct);
        }
        var result = await _reservationEngine.CreateReservationAsync(workspaceId, context.SelectedLocationId!, service.Id,
            phone, context.RealCustomerName!, DateTime.SpecifyKind(selectedTime, DateTimeKind.Unspecified), ct, sourceMessageId);
        if (result.IsSuccess)
        {
            var name = context.RealCustomerName;
            ClearDraft(context);
            return $"✅ ¡Todo listo, {name}! Tu reserva está confirmada:\n• Servicio: {service.Name}\n• Fecha: {selectedTime:dd/MM/yyyy}\n• Hora: {selectedTime:HH:mm}\n\n¡Te esperamos!";
        }
        context.TargetTime = null;
        if (result.Error.Code is "Reservation.Closed" or "Reservation.OutOfHours")
        {
            var reason = result.Error.Code == "Reservation.Closed" ? "Esta sede no atiende ese día." : "La hora indicada está fuera del horario de atención.";
            if (result.Error.Code == "Reservation.Closed") context.TargetDate = null;
            UpdateStep(context);
            return reason + "\n\n" + await WeeklyHoursAsync(workspaceId, context.SelectedLocationId!, ct) +
                "\n\nDime otra combinación de día y hora para verificar su disponibilidad.";
        }
        if (result.Error.Code is "Reservation.Conflict" or "Reservation.ConcurrencyConflict")
        {
            UpdateStep(context);
            return "Ese horario acaba de ser ocupado.\n\n" + await AvailabilityOptionsAsync(workspaceId, context, false, ct);
        }
        if (result.Error.Code.StartsWith("Service.", StringComparison.Ordinal))
        {
            context.SelectedServiceId = null;
            UpdateStep(context);
            return "Ese servicio ya no está disponible para reservar en esta sede.\n\n" +
                await ServiceOptionsAsync(workspaceId, context.SelectedLocationId!, ct);
        }
        if (result.Error.Code == "Location.NotFound")
        {
            context.SelectedLocationId = null; context.SelectedServiceId = null;
            UpdateStep(context);
            return "La sede ya no está disponible.\n\n" + await LocationOptionsAsync(workspaceId, "¿Cuál sede deseas elegir?", ct);
        }
        UpdateStep(context);
        return result.Error.Description + "\n\n" + await PromptAsync(workspaceId, context, false, ct);
    }

    public Task<string> GetContinuationAsync(Guid workspaceId, ConversationContextDto context, CancellationToken ct, bool concise = false)
    {
        // A catalog document/factual answer already supplied offerings. Resume
        // with one question without appending a second catalog to that answer.
        if (concise && context.CurrentStep == "COLLECT_SERVICE")
            return Task.FromResult("¿Qué servicio deseas reservar en la sede seleccionada?");
        if (concise && context.CurrentStep == "COLLECT_LOCATION")
            return Task.FromResult("¿En cuál sede deseas continuar con tu reserva?");
        return PromptAsync(workspaceId, context, true, ct);
    }

    private async Task<string> PromptAsync(Guid workspaceId, ConversationContextDto context, bool preserveDraft, CancellationToken ct)
    {
        if (!preserveDraft && context.CurrentStep == "COLLECT_SERVICE") context.ReservationServiceOffset = 0;
        var question = context.CurrentStep switch
        {
            "COLLECT_CUSTOMERNAME" => "Para registrar tu reserva, ¿cuál es tu nombre y apellido?",
            "COLLECT_LOCATION" => await LocationOptionsAsync(workspaceId, "¿En cuál sede deseas atenderte?", ct),
            "COLLECT_SERVICE" when !string.IsNullOrWhiteSpace(context.SelectedLocationId)
                => OfferingServiceReservationExtensions.FormatReservationOptions(
                    await ReservableServicesAsync(workspaceId, context.SelectedLocationId, ct), context.ReservationServiceOffset),
            "COLLECT_DATE" => "¿Para qué fecha deseas reservar? Puedes indicar el día o escribir 'mañana'.",
            "COLLECT_TIME" => await AvailabilityOptionsAsync(workspaceId, context, preserveDraft, ct),
            "CONFIRM" => $"Conservé tu reserva para el {context.TargetDate} a las {context.TargetTime}. ¿Deseas continuar con esos datos?",
            _ => "Para continuar con tu reserva, ¿cuál es tu nombre y apellido?"
        };
        if (!preserveDraft) context.LastQuestion = question;
        return question;
    }

    private async Task<string> LocationOptionsAsync(Guid workspaceId, string question, CancellationToken ct)
    {
        var locations = (await _locationRepo.GetLocationsAsync(workspaceId, ct)).ToList();
        if (locations.Count == 0) return "No hay sedes registradas disponibles para reservar.";
        return "Sedes disponibles:\n" + string.Join("\n", locations.Select(l => $"• {l.Name}")) + "\n\n" + question;
    }

    private async Task<string> ServiceOptionsAsync(Guid workspaceId, string locationId, CancellationToken ct)
        => OfferingServiceReservationExtensions.FormatReservationOptions(await ReservableServicesAsync(workspaceId, locationId, ct));

    private async Task<List<ServiceDto>> ReservableServicesAsync(Guid workspaceId, string locationId, CancellationToken ct)
    {
        return (await _offeringService.GetServicesAsync(workspaceId, locationId, null, ct))
            .Where(s => s.IsActive && s.RequiresReservation && s.DurationInMinutes >= 5)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    private async Task<bool> IsSelectedServiceValidAsync(Guid workspaceId, ConversationContextDto context, CancellationToken ct)
    {
        var service = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId!, ct);
        return service is { RequiresReservation: true, DurationInMinutes: >= 5 } &&
            await _offeringService.IsServiceAvailableAtLocationAsync(workspaceId, service.Id, context.SelectedLocationId!, ct);
    }

    private async Task<string> WeeklyHoursAsync(Guid workspaceId, string locationId, CancellationToken ct)
    {
        var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
        var location = locations.FirstOrDefault(l => l.Id == locationId);
        if (location == null) return "La sede seleccionada ya no está disponible.";
        var hours = await _hoursRepo.GetBusinessHoursAsync(workspaceId, locationId, ct);
        return $"Horario de atención de {location.Name}:\n" + BusinessHoursFormatter.Format(hours);
    }

    private async Task<string> AvailabilityOptionsAsync(Guid workspaceId, ConversationContextDto context, bool preserveDraft, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(context.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            if (!preserveDraft) { context.TargetDate = null; UpdateStep(context); }
            return "¿Para qué fecha deseas reservar? Indica un día válido.";
        }
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId) || string.IsNullOrWhiteSpace(context.SelectedServiceId))
            return "Indica primero la sede y el servicio que deseas reservar.";
        if (!await IsSelectedServiceValidAsync(workspaceId, context, ct))
        {
            if (!preserveDraft) { context.SelectedServiceId = null; context.TargetTime = null; UpdateStep(context); }
            return "Ese servicio ya no está disponible para reservar.\n\n" + await ServiceOptionsAsync(workspaceId, context.SelectedLocationId, ct);
        }
        var hours = (await _hoursRepo.GetBusinessHoursAsync(workspaceId, context.SelectedLocationId, ct)).ToList();
        var dayHours = hours.FirstOrDefault(h => h.DayOfWeek == (int)date.DayOfWeek);
        if (!BusinessHoursFormatter.IsOpen(dayHours))
        {
            if (!preserveDraft) { context.TargetDate = null; context.TargetTime = null; UpdateStep(context); }
            return "Esta sede no atiende ese día.\n\n" + BusinessHoursFormatter.Format(hours) +
                "\n\nDime otra combinación de día y hora para verificar su disponibilidad.";
        }
        var slots = (await _reservationEngine.GetAvailabilityAsync(workspaceId, context.SelectedLocationId, context.SelectedServiceId, date, ct))
            .Where(s => s.IsAvailable).OrderBy(s => s.StartTime).ToList();
        if (slots.Count == 0)
        {
            if (!preserveDraft) { context.TargetDate = null; context.TargetTime = null; UpdateStep(context); }
            return $"El negocio atiende el {date:dd/MM/yyyy}, pero ya no quedan horarios disponibles. ¿Qué otro día prefieres?";
        }
        var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
        catch (InvalidTimeZoneException) { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
        return $"Para el {date:dd/MM/yyyy} tengo disponibles:\n" +
            string.Join("\n", slots.Take(8).Select(s => $"• {TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone):HH:mm}")) +
            (slots.Count > 8 ? "\nHay más opciones; puedes escribir otra hora dentro del horario comercial y verificaré su disponibilidad." : "") +
            "\n\n¿Cuál horario prefieres?";
    }

    private static string RemoveFinalQuestion(string options)
    {
        var index = options.LastIndexOf('¿');
        return index < 0 ? options : options[..index].TrimEnd();
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

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, string sourceMessageId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMessageId);
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
        var ticketId = await _requestService.CreateRequestAsync(workspaceId, phone, conversationId, type, title, messageText, sourceMessageId, null, ct);
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
    private readonly ICatalogGenerationService _catalogGeneration;
    private readonly INotificationService _notifications;

    public ChatFlow(IKnowledgeService knowledgeService, IEntitlementService entitlementService,
        ILocationResolverService locationResolver, ILogger<ChatFlow> logger,
        ICatalogGenerationService catalogGeneration, INotificationService notifications)
    {
        _knowledgeService = knowledgeService;
        _locationResolver = locationResolver;
        _logger = logger;
        _entitlementService = entitlementService;
        _catalogGeneration = catalogGeneration;
        _notifications = notifications;
    }

    public async Task<ChatResponse> ProcessAsync(Guid workspaceId, string text, AiInterpretation interpretation, string? factualLocationId, CancellationToken ct)
    {
        var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var denied = interpretation.DeniedResponse ?? AiIntentAccess.GetDeniedResponse(interpretation.Intent, modules);
        if (denied != null) return new ChatResponse(denied);

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
        if (!topic.HasValue) return new ChatResponse("No puedo resolver esa consulta con la información disponible.");

        string? locationId = factualLocationId;
        if (topic != KnowledgeTopic.Faqs && !string.IsNullOrWhiteSpace(interpretation.Location))
        {
            try
            {
                locationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
                if (string.IsNullOrWhiteSpace(locationId))
                    return new ChatResponse(new KnowledgeResult { Status = KnowledgeStatus.NotFound, Source = topic.Value }.ToResponse());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Knowledge location resolution failed for workspace {WorkspaceId}.", workspaceId);
                return new ChatResponse(new KnowledgeResult { Status = KnowledgeStatus.Unavailable, Source = topic.Value }.ToResponse());
            }
        }

        string? fallbackIntroduction = null;
        var broad = (interpretation.Intent is ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery)
            && interpretation.QueryKind == OfferingQueryKind.Broad && string.IsNullOrWhiteSpace(interpretation.SearchTerm);
        if (broad)
        {
            var scope = interpretation.Intent == ConversationIntent.ProductQuery ? "PRODUCT" : "SERVICE";
            var artifact = await _catalogGeneration.GetArtifactAsync(workspaceId, scope, ct);
            if (artifact != null && (artifact.WorkspaceId != workspaceId || artifact.Scope != scope))
                throw new InvalidOperationException("Catalog artifact workspace/scope mismatch.");
            if (artifact?.Status == CatalogArtifactStatus.Current && Uri.TryCreate(artifact.PdfUrl, UriKind.Absolute, out var pdfUri)
                && pdfUri.Scheme is "https" or "http")
            {
                var caption = scope == "PRODUCT"
                    ? "Te comparto nuestro catálogo actualizado de productos 😊. También puedo ayudarte con ubicaciones, horarios o una consulta sobre algún producto específico."
                    : "Te comparto nuestro catálogo actualizado de servicios 😊. " +
                        (modules.Contains("RESERVATIONS") ? "Si deseas reservar, también puedo ayudarte paso a paso." : "También puedo ayudarte con información sobre un servicio específico.");
                return new ChatResponse(caption, artifact.PdfUrl, scope == "PRODUCT" ? "catalogo-productos.pdf" : "catalogo-servicios.pdf", scope);
            }
            if (artifact?.Status == CatalogArtifactStatus.Generating)
                fallbackIntroduction = "Nuestro catálogo se está actualizando. Mientras tanto, estas son algunas opciones disponibles:";
            else
            {
                await _notifications.NotifyCatalogUnavailableAsync(workspaceId, scope, ct);
                fallbackIntroduction = "Nuestro catálogo completo todavía no está disponible en PDF. Mientras tanto, te comparto algunas opciones:";
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
            if (result.Found) return new ChatResponse($"Esta es la información registrada del negocio:\n{result.Facts}\nSi necesitas otro dato, indícame cuál.");
        }

        // Business facts are returned verbatim from the factual query, never
        // expanded by an unconstrained generation step.
        if (fallbackIntroduction != null)
        {
            var facts = result.Found ? result.Facts : result.Status == KnowledgeStatus.Unavailable
                ? result.ToResponse() : "No hay opciones registradas disponibles para esta consulta.";
            var next = interpretation.Intent == ConversationIntent.ServiceQuery && modules.Contains("RESERVATIONS")
                ? "Si deseas reservar, dime qué servicio te interesa." : "También puedo ayudarte con ubicaciones, horarios o información específica. ¿Qué dato necesitas?";
            return new ChatResponse($"{fallbackIntroduction}\n\n{facts}\n\n{next}");
        }
        return new ChatResponse(result.Status == KnowledgeStatus.NotFound ? "No tengo información registrada que coincida con tu consulta." : result.ToResponse());
    }
}

