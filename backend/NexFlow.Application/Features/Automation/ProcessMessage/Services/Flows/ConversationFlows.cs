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

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

// --- INTERFACES ---
public interface IBookingFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct); }
public interface IRequestFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, CancellationToken ct); }
public interface ISupportFlow { Task<string> ProcessAsync(Guid workspaceId, string conversationId, CancellationToken ct); }
// 🔥 RAG FIX: Añadimos WorkspaceId para que la IA sepa a qué empresa pertenece el catálogo
public interface IChatFlow { Task<string> ProcessAsync(Guid workspaceId, string text, CancellationToken ct); }

// --- IMPLEMENTACIONES ---
public class BookingFlow : IBookingFlow
{
    private readonly IOfferingService _offeringService;
    private readonly ILocationResolverService _locationResolver;
    private readonly IReservationEngine _reservationEngine;
    private readonly ILocationRepository _locationRepo;
    private readonly IBusinessHoursRepository _hoursRepo;
    private readonly ICatalogArtifactRepository _artifactRepo;
    private readonly IMessageGateway _messageGateway;
    private readonly IConversationRepository _conversationRepo;

    public BookingFlow(
        IOfferingService offeringService, ILocationResolverService locationResolver,
        IReservationEngine reservationEngine, ILocationRepository locationRepo,
        IBusinessHoursRepository hoursRepo, ICatalogArtifactRepository artifactRepo,
        IMessageGateway messageGateway, IConversationRepository conversationRepo)
    {
        _offeringService = offeringService; _locationResolver = locationResolver;
        _reservationEngine = reservationEngine; _locationRepo = locationRepo;
        _hoursRepo = hoursRepo; _artifactRepo = artifactRepo;
        _messageGateway = messageGateway; _conversationRepo = conversationRepo;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct)
    {
        context.CurrentGoal = "BOOKING";
        context.LastIntent = interpretation.Intent;

        // =================================================================
        // 1. ACTUALIZACIÓN DE MEMORIA Y DETECCIÓN DE CORRECCIONES HUMANAS
        // =================================================================
        bool requiresTimeReset = false;

        if (!string.IsNullOrWhiteSpace(interpretation.CustomerName))
            context.RealCustomerName = interpretation.CustomerName;

        if (!string.IsNullOrWhiteSpace(interpretation.Service) && context.SelectedServiceId != interpretation.Service)
        {
            context.SelectedServiceId = interpretation.Service;
            requiresTimeReset = true;
        }

        if (!string.IsNullOrWhiteSpace(interpretation.Location))
        {
            var realLocationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
            if (realLocationId != null && context.SelectedLocationId != realLocationId)
            {
                context.SelectedLocationId = realLocationId;
                requiresTimeReset = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(interpretation.Date) && context.TargetDate != interpretation.Date)
        {
            context.TargetDate = interpretation.Date;
            requiresTimeReset = true;
        }

        if (requiresTimeReset && string.IsNullOrWhiteSpace(interpretation.Time))
        {
            context.TargetTime = null;
        }

        if (!string.IsNullOrWhiteSpace(interpretation.Time))
            context.TargetTime = interpretation.Time;


        // =================================================================
        // 2. EVALUACIÓN DE RANURAS (SLOTS) FALTANTES
        // =================================================================
        context.MissingFields.Clear();
        if (string.IsNullOrWhiteSpace(context.RealCustomerName)) context.MissingFields.Add("CustomerName");
        if (string.IsNullOrWhiteSpace(context.SelectedServiceId)) context.MissingFields.Add("Service");
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId)) context.MissingFields.Add("Location");
        if (string.IsNullOrWhiteSpace(context.TargetDate)) context.MissingFields.Add("Date");
        if (string.IsNullOrWhiteSpace(context.TargetTime)) context.MissingFields.Add("Time");

        if (!context.MissingFields.Any())
            context.CurrentStep = "CONFIRM";
        else
            context.CurrentStep = $"COLLECT_{context.MissingFields.First().ToUpper()}";


        // =================================================================
        // 3. MÁQUINA DE ESTADOS
        // =================================================================
        switch (context.CurrentStep)
        {
            case "COLLECT_CUSTOMERNAME":
                context.LastQuestion = "¿me podrías indicar tu *nombre y apellido*?";
                return $"¡Excelente! Te ayudaré a agendar tu cita. 📅\n\nPara poder registrarte correctamente, {context.LastQuestion}";

            case "COLLECT_SERVICE":
                var artifact = await _artifactRepo.GetCurrentArtifactAsync(workspaceId, "SERVICE", ct);
                bool pdfSentSuccessfully = false;

                if (artifact != null && artifact.Status == CatalogArtifactStatus.Current && !string.IsNullOrWhiteSpace(artifact.PdfUrl))
                {
                    var docPendingId = Guid.NewGuid().ToString();
                    await _conversationRepo.AddMessageAsync(workspaceId, conversationId, new MessageRecord { Id = docPendingId, ExternalMessageId = docPendingId, Direction = "outbound", Sender = SenderType.AI, Content = "DOCUMENTO PDF ENVIADO", Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow }, ct);

                    try
                    {
                        var extId = await _messageGateway.SendDocumentAsync(workspaceId, phone, artifact.PdfUrl, "Catalogo_Servicios.pdf", "Aquí tienes nuestro catálogo detallado de servicios 📄", docPendingId, ct);
                        await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, docPendingId, MessageStatus.Sent, extId, ct);
                        pdfSentSuccessfully = true;
                    }
                    catch { await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, docPendingId, MessageStatus.Failed, null, ct); }
                }

                var availableServices = await _offeringService.SearchOfferingsAsync(workspaceId, null, "SERVICE", null, ct);
                if (!availableServices.Any()) return "Actualmente no contamos con servicios habilitados para reservas. Por favor, intenta más tarde.";

                var serviceList = string.Join("\n", availableServices.Take(5).Select(s => $"- {s.Name}"));
                var introText = pdfSentSuccessfully ? "Te acabo de enviar nuestro catálogo completo." : "Tuvimos un pequeño problema al cargar el PDF, pero aquí tienes";
                string extra = (pdfSentSuccessfully && availableServices.Count() > 5) ? "\n*(Y más servicios en nuestro catálogo PDF)*" : "";

                context.LastQuestion = "¿qué servicio deseas reservar?";
                return $"¡Gracias, {context.RealCustomerName}! \n\n{introText} algunos de los servicios más solicitados:\n{serviceList}{extra}\n\n👉 *Por favor, {context.LastQuestion}*";

            case "COLLECT_LOCATION":
                var targetSrv = (await _offeringService.SearchOfferingsAsync(workspaceId, null, "SERVICE", context.SelectedServiceId, ct)).FirstOrDefault();
                if (targetSrv == null)
                {
                    context.SelectedServiceId = null;
                    return $"Disculpa, no logré encontrar ese servicio. ¿Podrías escribir el nombre nuevamente?";
                }

                var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
                var locationList = string.Join("\n", locations.Select(l => $"- {l.Name}"));

                context.LastQuestion = "¿En cuál de nuestras sedes te gustaría atenderte?";
                return $"Has elegido *{targetSrv.Name}*.\n\n{context.LastQuestion}\n{locationList}\n\n👉 *Escribe tu sede preferida.*";

            case "COLLECT_DATE":
                var validSrv = (await _offeringService.SearchOfferingsAsync(workspaceId, null, "SERVICE", context.SelectedServiceId, ct)).First();
                bool isAvailable = await _offeringService.IsAvailableAtLocationAsync(workspaceId, validSrv.Id, context.SelectedLocationId!, ct);

                if (!isAvailable)
                {
                    context.SelectedLocationId = null;
                    return $"El servicio de {validSrv.Name} no está disponible en esa sede. ¿Te gustaría intentar en otra?";
                }

                context.LastQuestion = "¿Para qué fecha te gustaría programar tu cita?";
                return $"¡Excelente! 🏥\n\n{context.LastQuestion}\n👉 *(Ej: 'mañana', 'el próximo viernes', o 'el 25 de octubre').*";

            case "COLLECT_TIME":
                if (!DateTime.TryParse(context.TargetDate, out var parsedDate))
                {
                    context.TargetDate = null;
                    return "No logré entender la fecha. ¿Podrías decírmela en formato YYYY-MM-DD o 'mañana'?";
                }

                var hours = await _hoursRepo.GetBusinessHoursAsync(workspaceId, context.SelectedLocationId!, ct);
                var dayHours = hours.FirstOrDefault(h => h.DayOfWeek == (int)parsedDate.DayOfWeek);

                if (dayHours == null || dayHours.IsClosed)
                {
                    context.TargetDate = null;
                    return $"Ese día nos encontramos cerrados. ¿Te gustaría intentar con otra fecha?";
                }

                var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, context.SelectedLocationId!, context.SelectedServiceId!, parsedDate, ct);
                if (!slots.Any())
                {
                    context.TargetDate = null;
                    return $"Lo lamento mucho, pero tenemos la agenda llena el {parsedDate:dd/MM/yyyy}. ¿Intentamos otro día?";
                }

                context.LastQuestion = "¿A qué hora prefieres que te agendemos?";
                return $"Para el *{parsedDate:dd/MM/yyyy}*, nuestro horario de atención es de *{dayHours.OpenTime} a {dayHours.CloseTime}*.\n\n👉 *Tenemos turnos disponibles, {context.LastQuestion} (Ej: 'a las 10:00 am')*";

            case "CONFIRM":
                var finalSrv = (await _offeringService.SearchOfferingsAsync(workspaceId, null, "SERVICE", context.SelectedServiceId, ct)).First();
                var rawDateTime = $"{context.TargetDate} {context.TargetTime}";

                if (DateTime.TryParse(rawDateTime, out var exactDateTime))
                {
                    var result = await _reservationEngine.CreateReservationAsync(workspaceId, context.SelectedLocationId!, finalSrv.Id, phone, context.RealCustomerName!, exactDateTime, ct);
                    if (result.IsSuccess)
                    {
                        context.CurrentGoal = null;
                        return $"✅ *¡Todo listo, {context.RealCustomerName}!*\n\nTu cita ha sido confirmada exitosamente:\n🦷 Servicio: *{finalSrv.Name}*\n📅 Fecha: *{exactDateTime:dd/MM/yyyy}*\n⏰ Hora: *{exactDateTime:HH:mm}*\n\n¡Te esperamos! Si necesitas cancelar o reagendar, solo dímelo.";
                    }
                    context.TargetTime = null;
                    return $"Tuvimos un inconveniente: {result.Error.Description}. Por favor, indícame otro horario.";
                }

                context.TargetTime = null;
                return "Hubo un error al procesar el horario. ¿Podrías indicarme la hora nuevamente?";

            default:
                return "Estoy procesando tu solicitud...";
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
        _aiRouter = aiRouter;
        _offeringService = offeringService;
        _profileRepo = profileRepo;
        _logger = logger;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string text, CancellationToken ct)
    {
        string businessName = "nuestra empresa";
        string catalogContext = "Actualmente no tenemos catálogo registrado.";

        try
        {
            // 1. Obtenemos el nombre comercial real del negocio
            var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
            if (profile != null && !string.IsNullOrWhiteSpace(profile.CommercialName))
            {
                businessName = profile.CommercialName;
            }

            // 2. Traemos TODO el catálogo (Productos y Servicios) del Tenant actual
            var offerings = await _offeringService.SearchOfferingsAsync(workspaceId, null, null, null, ct);

            if (offerings != null && offerings.Any())
            {
                // Blindaje: Solo pasamos los primeros 50 ítems. 
                // Convertimos PriceMinorUnits a moneda real (dividimos entre 100m)
                catalogContext = string.Join("\n", offerings.Take(50).Select(o =>
                    $"- {o.Name}: {(o.Currency ?? "PEN")} {(o.PriceMinorUnits / 100m):0.00}. {(string.IsNullOrWhiteSpace(o.Description) ? "" : $"Detalles: {o.Description}")}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al recuperar el catálogo o perfil para el workspace {WorkspaceId} en ChatFlow.", workspaceId);
            // No bloqueamos, permitimos que el Chat continúe de forma genérica
        }

        // 3. System Prompt con Inyección de Contexto RAG
        var systemPrompt = $@"Eres el asistente virtual de ventas y atención al cliente de '{businessName}'.
Tu objetivo es responder de forma amable, persuasiva y concisa a las dudas del cliente.

AQUÍ ESTÁ NUESTRO CATÁLOGO ACTUAL DE PRODUCTOS Y SERVICIOS CON PRECIOS REALES:
{catalogContext}

REGLAS ESTRICTAS:
1. SIEMPRE responde basándote en los datos del catálogo de arriba.
2. Si el cliente pregunta por un precio (Ej: concreto, limpieza, etc.), dale el precio exacto extraído de la lista.
3. Si el cliente pregunta por un producto o servicio que NO está en la lista, indícale educadamente que no contamos con ello por el momento. ¡NUNCA INVENTES PRECIOS!
4. Mantén tus respuestas precisas, cálidas y cortas (ideales para leer en WhatsApp).";

        return await _aiRouter.ExecuteTaskAsync(AiTaskType.ComplexChat, systemPrompt, text, false, ct);
    }
}