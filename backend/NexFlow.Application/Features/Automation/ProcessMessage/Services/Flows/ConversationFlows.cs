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

// --- INTERFACES ---
public interface IBookingFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct); }
public interface IRequestFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, CancellationToken ct); }
public interface ISupportFlow { Task<string> ProcessAsync(Guid workspaceId, string conversationId, CancellationToken ct); }

// 🔥 SPRINT 03/05: Ahora IChatFlow recibe la Interpretación para saber qué buscar
public interface IChatFlow { Task<string> ProcessAsync(Guid workspaceId, string text, AiInterpretation interpretation, CancellationToken ct); }

// --- IMPLEMENTACIONES ---
public class BookingFlow : IBookingFlow
{
    // 🔥 SPRINT 3: Protegemos también las invocaciones directas del flujo.
    private readonly IEntitlementService _entitlementService;
    private readonly IOfferingService _offeringService;
    private readonly ILocationResolverService _locationResolver;
    private readonly IReservationEngine _reservationEngine;
    private readonly ILocationRepository _locationRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly IMessageGateway _messageGateway;

    public BookingFlow(
        IOfferingService offeringService, ILocationResolverService locationResolver,
        IReservationEngine reservationEngine, ILocationRepository locationRepo,
        IBusinessHoursRepository hoursRepo, IMessageGateway messageGateway, IEntitlementService entitlementService)
    {
        _offeringService = offeringService; _locationResolver = locationResolver;
        _reservationEngine = reservationEngine; _locationRepo = locationRepo;
        _hoursRepo = hoursRepo; _messageGateway = messageGateway;
        _entitlementService = entitlementService;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct)
    {
        // 🔥 SPRINT 3: Ninguna consulta de servicios o sedes precede a la autorización.
        var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deniedResponse = OfferingQueryAccess.GetDeniedResponse(interpretation.Intent, activeModules);
        if (deniedResponse != null) return deniedResponse;
        if (!activeModules.Contains("SERVICES")) return OfferingQueryAccess.ServicesUnavailable;
        if (!activeModules.Contains("RESERVATIONS")) return "No hay información de reservas disponible en este momento.";

        // Se mantiene la lógica de Booking intacta, solo actualizamos el Intent
        context.CurrentGoal = "RESERVATION";
        context.LastIntent = interpretation.Intent;

        bool requiresTimeReset = false;

        if (!string.IsNullOrWhiteSpace(interpretation.CustomerName)) context.RealCustomerName = interpretation.CustomerName;
        if (!string.IsNullOrWhiteSpace(interpretation.Location))
        {
            var realLocationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
            // 🔥 SPRINT 2: Una sede no resuelta no puede conservar una selección anterior.
            if (realLocationId == null)
            {
                context.SelectedLocationId = null;
                context.SelectedServiceId = null;
                context.TargetTime = null;
                context.MissingFields.Clear();
                context.MissingFields.Add("Location");
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

        // 🔥 SPRINT 2: Validamos también las selecciones recuperadas del contexto.
        string? serviceClarification = null;
        if (!string.IsNullOrWhiteSpace(context.SelectedLocationId))
        {
            var workspaceLocations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
            if (!workspaceLocations.Any(l => l.Id == context.SelectedLocationId))
            {
                context.SelectedLocationId = null;
                context.SelectedServiceId = null;
                context.TargetTime = null;
            }
            else if (!string.IsNullOrWhiteSpace(interpretation.Service))
            {
                var resolution = await _offeringService.ResolveReservationServiceAsync(
                    workspaceId, context.SelectedLocationId, interpretation.Service, ct);
                if (context.SelectedServiceId != resolution.Service?.Id && string.IsNullOrWhiteSpace(interpretation.Time))
                    context.TargetTime = null;
                context.SelectedServiceId = resolution.Service?.Id;
                serviceClarification = resolution.Clarification;
            }
            else if (!string.IsNullOrWhiteSpace(context.SelectedServiceId))
            {
                var selectedService = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId, ct);
                if (selectedService == null || !selectedService.RequiresReservation
                    || !await _offeringService.IsServiceAvailableAtLocationAsync(workspaceId, context.SelectedServiceId, context.SelectedLocationId, ct))
                {
                    context.SelectedServiceId = null;
                    context.TargetTime = null;
                    serviceClarification = "El servicio seleccionado ya no está disponible para reservar en esta sede. ¿Qué servicio deseas reservar?";
                }
            }
        }

        context.MissingFields.Clear();
        if (string.IsNullOrWhiteSpace(context.RealCustomerName)) context.MissingFields.Add("CustomerName");
        // 🔥 SPRINT 2: Resolvemos primero la sede para no confundir servicios homónimos.
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId)) context.MissingFields.Add("Location");
        if (string.IsNullOrWhiteSpace(context.SelectedServiceId)) context.MissingFields.Add("Service");
        if (string.IsNullOrWhiteSpace(context.TargetDate)) context.MissingFields.Add("Date");
        if (string.IsNullOrWhiteSpace(context.TargetTime)) context.MissingFields.Add("Time");

        if (!context.MissingFields.Any()) context.CurrentStep = "CONFIRM";
        else context.CurrentStep = $"COLLECT_{context.MissingFields.First().ToUpper()}";

        // 🔥 SPRINT 2: Ausencia o ambigüedad requieren aclaración, nunca un ID inventado.
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
                // 🔥 SPRINT 2: Mostramos únicamente servicios reservables en la sede elegida.
                var availableServices = (await _offeringService.GetServicesAsync(workspaceId, context.SelectedLocationId, null, ct))
                    .Where(s => s.RequiresReservation).ToList();
                if (!availableServices.Any()) return "Actualmente no contamos con servicios habilitados para reservas.";
                var serviceList = string.Join("\n", availableServices.Take(5).Select(s => $"- {s.Name}"));
                context.LastQuestion = "¿qué servicio deseas reservar?";
                return $"¡Gracias, {context.RealCustomerName}! \n\nAquí tienes algunos servicios solicitados:\n{serviceList}\n\n👉 *Por favor, {context.LastQuestion}*";

            case "COLLECT_LOCATION":
                // 🔥 SPRINT 2: La sede se solicita antes de resolver el servicio.
                var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
                if (!string.IsNullOrWhiteSpace(context.SelectedServiceId))
                {
                    var selectedService = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId, ct);
                    if (selectedService == null || !selectedService.RequiresReservation)
                        return "El servicio seleccionado no está disponible para reservar.";
                    locations = locations.Where(l =>
                        string.Equals(selectedService.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase)
                        || (l.Id != null && selectedService.LocationIds?.Contains(l.Id) == true)).ToList();
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
                // 🔥 SPRINT 2: Revalidamos antes de confirmar para evitar desreferenciar una entidad inexistente.
                if (finalSrv == null || !finalSrv.RequiresReservation
                    || !await _offeringService.IsServiceAvailableAtLocationAsync(workspaceId, finalSrv.Id, context.SelectedLocationId!, ct))
                {
                    context.SelectedServiceId = null;
                    context.TargetTime = null;
                    context.MissingFields.Add("Service");
                    context.CurrentStep = "COLLECT_SERVICE";
                    context.LastQuestion = "El servicio ya no está disponible en esta sede. ¿Qué otro servicio deseas reservar?";
                    return context.LastQuestion;
                }
                var rawDateTime = $"{context.TargetDate} {context.TargetTime}";

                if (DateTime.TryParse(rawDateTime, out var exactDateTime))
                {
                    var result = await _reservationEngine.CreateReservationAsync(workspaceId, context.SelectedLocationId!, finalSrv!.Id, phone, context.RealCustomerName!, exactDateTime, ct);
                    if (result.IsSuccess)
                    {
                        context.CurrentGoal = null;
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
        _requestService = requestService;
        _handoffService = handoffService;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, CancellationToken ct)
    {
        // 🔥 Heurística ligera para clasificar la solicitud desde el texto original
        var type = RequestType.Support;
        var title = "Solicitud de Atención";
        var lowerText = messageText.ToLowerInvariant();

        if (lowerText.Contains("comprar") || lowerText.Contains("cotizar") || lowerText.Contains("precio") || lowerText.Contains("mayor"))
        {
            type = RequestType.CommercialInquiry;
            title = "Consulta Comercial / Cotización";
        }
        else if (lowerText.Contains("tramite") || lowerText.Contains("afiliar") || lowerText.Contains("documento"))
        {
            type = RequestType.Tramite;
            title = "Trámite Administrativo";
        }

        var ticketId = await _requestService.CreateRequestAsync(workspaceId, phone, conversationId, type, title, messageText, null, ct);

        // Escalar a humano silenciando a la IA temporalmente
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
    private readonly ILocationResolverService _locationResolver;
    private readonly IEntitlementService _entitlementService;
    private readonly IAiRouter _aiRouter;
    private readonly IOfferingService _offeringService;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly ILogger<ChatFlow> _logger;

    public ChatFlow(IAiRouter aiRouter, IOfferingService offeringService, IBusinessProfileRepository profileRepo, ILogger<ChatFlow> logger,
        IEntitlementService entitlementService, ILocationResolverService locationResolver)
    {
        _aiRouter = aiRouter; _offeringService = offeringService;
        _profileRepo = profileRepo; _logger = logger;
        _entitlementService = entitlementService;
        _locationResolver = locationResolver;
    }

    // 🔥 SPRINT 03/05: Ahora recibe la interpretación y busca de forma atómica
    public async Task<string> ProcessAsync(Guid workspaceId, string text, AiInterpretation interpretation, CancellationToken ct)
    {
        string InformationUnavailable()
        {
            interpretation.Intent = "SUPPORT";
            return "No tengo esa información en este momento";
        }

        string businessName;
        string? extractedData = null;

        try
        {
            // 🔥 SPRINT 3: La denegación retorna antes de consultar datos o invocar al modelo.
            var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var deniedResponse = OfferingQueryAccess.GetDeniedResponse(interpretation.Intent, activeModules);
            if (deniedResponse != null) return deniedResponse;
            var intent = interpretation.Intent?.Trim().ToUpperInvariant();

            string? locationId = null;
            if (!string.IsNullOrWhiteSpace(interpretation.Location))
            {
                locationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
                if (string.IsNullOrWhiteSpace(locationId))
                    return "No pude identificar la sede. ¿Podrías indicar su nombre exacto?";
            }

            var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
            if (profile == null || string.IsNullOrWhiteSpace(profile.CommercialName))
                return InformationUnavailable();
            businessName = profile.CommercialName;

            // 🔥 BÚSQUEDA INTELIGENTE: Si la IA sabe qué busca, no traemos todo el catálogo.
            if (intent == "PRODUCT_QUERY")
            {
                var products = await _offeringService.GetProductsAsync(workspaceId, locationId, interpretation.SearchTerm, ct);
                if (products.Any())
                    extractedData = string.Join("\n", products.Take(10).Select(p => $"- {p.Name}: {p.Currency} {(p.PriceMinorUnits / 100m):0.00}. {p.Description}"));
                else
                    return InformationUnavailable();
            }
            else if (intent == "SERVICE_QUERY")
            {
                var services = await _offeringService.GetServicesAsync(workspaceId, locationId, interpretation.SearchTerm, ct);
                if (services.Any())
                    extractedData = string.Join("\n", services.Take(10).Select(s => $"- {s.Name}: {s.Currency} {(s.PriceMinorUnits / 100m):0.00}. {s.Description}"));
                else
                    return InformationUnavailable();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al recuperar datos para el workspace {WorkspaceId} en ChatFlow.", workspaceId);
            return InformationUnavailable();
        }

        if (string.IsNullOrWhiteSpace(extractedData)) return InformationUnavailable();

        var systemPrompt = $@"Eres el asistente virtual de ventas y atención al cliente de '{businessName}'.
Tu objetivo es responder de forma amable, persuasiva y concisa a las dudas del cliente.

AQUÍ ESTÁN LOS DATOS QUE ENCONTRÉ EN LA BASE DE DATOS SOBRE LO QUE PREGUNTÓ EL CLIENTE:
{extractedData}

REGLAS ESTRICTAS:
1. SIEMPRE responde basándote en los datos de arriba.
2. Si el cliente pregunta por un precio, dale el precio exacto extraído de la lista.
3. Si la lista dice que no tenemos el producto, indícale educadamente que no contamos con ello. ¡NUNCA INVENTES PRECIOS!
4. Mantén tus respuestas precisas, cálidas y cortas (ideales para leer en WhatsApp).";

        return await _aiRouter.ExecuteTaskAsync(AiTaskType.ComplexChat, systemPrompt, text, false, ct);
    }
}

// 🔥 SPRINT 3: Matriz única y determinista compartida por el orquestador y los flujos.
internal static class OfferingQueryAccess
{
    internal const string ProductsUnavailable = "No hay información de productos disponible en este momento.";
    internal const string ServicesUnavailable = "No hay información de servicios disponible en este momento.";

    internal static string? GetDeniedResponse(string? intent, ISet<string> activeModules) =>
        intent?.Trim().ToUpperInvariant() switch
        {
            "PRODUCT_QUERY" when !activeModules.Contains("CATALOG") => ProductsUnavailable,
            "SERVICE_QUERY" when !activeModules.Contains("SERVICES") => ServicesUnavailable,
            _ => null
        };
}
