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
    private readonly IOfferingService _offeringService;
    private readonly ILocationResolverService _locationResolver;
    private readonly IReservationEngine _reservationEngine;
    private readonly ILocationRepository _locationRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly IMessageGateway _messageGateway;

    public BookingFlow(
        IOfferingService offeringService, ILocationResolverService locationResolver,
        IReservationEngine reservationEngine, ILocationRepository locationRepo,
        IBusinessHoursRepository hoursRepo, IMessageGateway messageGateway)
    {
        _offeringService = offeringService; _locationResolver = locationResolver;
        _reservationEngine = reservationEngine; _locationRepo = locationRepo;
        _hoursRepo = hoursRepo; _messageGateway = messageGateway;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct)
    {
        // Se mantiene la lógica de Booking intacta, solo actualizamos el Intent
        context.CurrentGoal = "RESERVATION";
        context.LastIntent = interpretation.Intent;

        bool requiresTimeReset = false;

        if (!string.IsNullOrWhiteSpace(interpretation.CustomerName)) context.RealCustomerName = interpretation.CustomerName;
        if (!string.IsNullOrWhiteSpace(interpretation.Service) && context.SelectedServiceId != interpretation.Service)
        {
            context.SelectedServiceId = interpretation.Service; requiresTimeReset = true;
        }

        if (!string.IsNullOrWhiteSpace(interpretation.Location))
        {
            var realLocationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
            if (realLocationId != null && context.SelectedLocationId != realLocationId)
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

        context.MissingFields.Clear();
        if (string.IsNullOrWhiteSpace(context.RealCustomerName)) context.MissingFields.Add("CustomerName");
        if (string.IsNullOrWhiteSpace(context.SelectedServiceId)) context.MissingFields.Add("Service");
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId)) context.MissingFields.Add("Location");
        if (string.IsNullOrWhiteSpace(context.TargetDate)) context.MissingFields.Add("Date");
        if (string.IsNullOrWhiteSpace(context.TargetTime)) context.MissingFields.Add("Time");

        if (!context.MissingFields.Any()) context.CurrentStep = "CONFIRM";
        else context.CurrentStep = $"COLLECT_{context.MissingFields.First().ToUpper()}";

        switch (context.CurrentStep)
        {
            case "COLLECT_CUSTOMERNAME":
                context.LastQuestion = "¿me podrías indicar tu *nombre y apellido*?";
                return $"¡Excelente! Te ayudaré a agendar tu cita. 📅\n\nPara poder registrarte correctamente, {context.LastQuestion}";

            case "COLLECT_SERVICE":
                var availableServices = await _offeringService.GetServicesAsync(workspaceId, null, null, ct);
                if (!availableServices.Any()) return "Actualmente no contamos con servicios habilitados para reservas.";
                var serviceList = string.Join("\n", availableServices.Take(5).Select(s => $"- {s.Name}"));
                context.LastQuestion = "¿qué servicio deseas reservar?";
                return $"¡Gracias, {context.RealCustomerName}! \n\nAquí tienes algunos servicios solicitados:\n{serviceList}\n\n👉 *Por favor, {context.LastQuestion}*";

            case "COLLECT_LOCATION":
                var targetSrv = await _offeringService.GetServiceByIdAsync(workspaceId, context.SelectedServiceId!, ct);
                if (targetSrv == null) { context.SelectedServiceId = null; return $"No encontré ese servicio. ¿Podrías escribir el nombre nuevamente?"; }
                var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
                var locationList = string.Join("\n", locations.Select(l => $"- {l.Name}"));
                context.LastQuestion = "¿En cuál de nuestras sedes te gustaría atenderte?";
                return $"Has elegido *{targetSrv.Name}*.\n\n{context.LastQuestion}\n{locationList}";

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
        _requestService = requestService; _handoffService = handoffService;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, CancellationToken ct)
    {
        var ticketId = await _requestService.CreateSupportTicketAsync(workspaceId, phone, messageText, ct);
        await _handoffService.EscalateToHumanAsync(workspaceId, conversationId, HandoffReason.AiEscalation, ct);
        return $"He registrado tu solicitud con el código {ticketId}. Un asesor se pondrá en contacto contigo a la brevedad.";
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
    private readonly IAiRouter _aiRouter;
    private readonly IOfferingService _offeringService;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly ILogger<ChatFlow> _logger;

    public ChatFlow(IAiRouter aiRouter, IOfferingService offeringService, IBusinessProfileRepository profileRepo, ILogger<ChatFlow> logger)
    {
        _aiRouter = aiRouter; _offeringService = offeringService;
        _profileRepo = profileRepo; _logger = logger;
    }

    // 🔥 SPRINT 03/05: Ahora recibe la interpretación y busca de forma atómica
    public async Task<string> ProcessAsync(Guid workspaceId, string text, AiInterpretation interpretation, CancellationToken ct)
    {
        string businessName = "nuestra empresa";
        string extractedData = "No tengo información específica sobre eso en este momento.";

        try
        {
            var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
            if (profile != null && !string.IsNullOrWhiteSpace(profile.CommercialName))
                businessName = profile.CommercialName;

            // 🔥 BÚSQUEDA INTELIGENTE: Si la IA sabe qué busca, no traemos todo el catálogo.
            if (interpretation.Intent == "PRODUCT_QUERY")
            {
                var products = await _offeringService.GetProductsAsync(workspaceId, interpretation.Location, interpretation.SearchTerm, ct);
                if (products.Any())
                    extractedData = string.Join("\n", products.Take(10).Select(p => $"- {p.Name}: {p.Currency} {(p.PriceMinorUnits / 100m):0.00}. {p.Description}"));
                else
                    extractedData = "Dile al cliente educadamente que no tenemos ese producto en nuestro catálogo.";
            }
            else if (interpretation.Intent == "SERVICE_QUERY")
            {
                var services = await _offeringService.GetServicesAsync(workspaceId, interpretation.Location, interpretation.SearchTerm, ct);
                if (services.Any())
                    extractedData = string.Join("\n", services.Take(10).Select(s => $"- {s.Name}: {s.Currency} {(s.PriceMinorUnits / 100m):0.00}. {s.Description}"));
                else
                    extractedData = "Dile al cliente educadamente que no realizamos ese servicio.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al recuperar datos para el workspace {WorkspaceId} en ChatFlow.", workspaceId);
        }

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