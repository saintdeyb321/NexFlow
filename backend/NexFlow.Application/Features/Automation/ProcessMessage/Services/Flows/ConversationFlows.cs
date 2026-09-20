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

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

// --- INTERFACES ---
public interface IBookingFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct); }
public interface IRequestFlow { Task<string> ProcessAsync(Guid workspaceId, string phone, string messageText, string conversationId, CancellationToken ct); }
public interface ISupportFlow { Task<string> ProcessAsync(Guid workspaceId, string conversationId, CancellationToken ct); }
public interface IChatFlow { Task<string> ProcessAsync(string text, CancellationToken ct); }

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

    public BookingFlow(
        IOfferingService offeringService, ILocationResolverService locationResolver,
        IReservationEngine reservationEngine, ILocationRepository locationRepo,
        IBusinessHoursRepository hoursRepo, ICatalogArtifactRepository artifactRepo,
        IMessageGateway messageGateway)
    {
        _offeringService = offeringService;
        _locationResolver = locationResolver;
        _reservationEngine = reservationEngine;
        _locationRepo = locationRepo;
        _hoursRepo = hoursRepo;
        _artifactRepo = artifactRepo;
        _messageGateway = messageGateway;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, ConversationContextDto context, AiInterpretation interpretation, string fallbackName, CancellationToken ct)
    {
        context.CurrentGoal = "BOOKING";

        // Actualizamos memoria
        if (!string.IsNullOrWhiteSpace(interpretation.Service)) context.SelectedServiceId = interpretation.Service;
        if (!string.IsNullOrWhiteSpace(interpretation.Date)) context.TargetDate = interpretation.Date;
        if (!string.IsNullOrWhiteSpace(interpretation.Time)) context.TargetTime = interpretation.Time;
        if (!string.IsNullOrWhiteSpace(interpretation.CustomerName)) context.RealCustomerName = interpretation.CustomerName;

        if (!string.IsNullOrWhiteSpace(interpretation.Location))
        {
            var realLocationId = await _locationResolver.ResolveLocationIdAsync(workspaceId, interpretation.Location, ct);
            if (realLocationId != null) context.SelectedLocationId = realLocationId;
        }

        // PASO 0: Preguntar el nombre real
        if (string.IsNullOrWhiteSpace(context.RealCustomerName))
        {
            return "¡Excelente! Te ayudaré a agendar tu cita. 📅\n\nPara poder registrarte correctamente, ¿me podrías indicar tu *nombre y apellido*?";
        }

        // PASO 1: Pedir el Servicio y Enviar el PDF
        if (string.IsNullOrWhiteSpace(context.SelectedServiceId))
        {
            // Enviamos el PDF si existe
            var artifact = await _artifactRepo.GetCurrentArtifactAsync(workspaceId, "SERVICE", ct);
            if (artifact != null && !string.IsNullOrWhiteSpace(artifact.PdfUrl))
            {
                // Disparamos el PDF en segundo plano (Fire and forget seguro) para que llegue justo antes del texto
                _ = _messageGateway.SendDocumentAsync(workspaceId, phone, artifact.PdfUrl, "Catalogo_Servicios.pdf", "Aquí tienes nuestro catálogo detallado de servicios 📄", "pdf_" + Guid.NewGuid().ToString(), ct);
            }

            var availableServices = await _offeringService.SearchOfferingsAsync(workspaceId, null, "SERVICE", null, ct);
            if (!availableServices.Any()) return "Actualmente no contamos con servicios habilitados para reservas. Por favor, intenta más tarde.";

            var serviceList = string.Join("\n", availableServices.Take(5).Select(s => $"- {s.Name}"));
            string extra = availableServices.Count() > 5 ? "\n*(Y más servicios en nuestro catálogo PDF)*" : "";

            return $"¡Gracias, {context.RealCustomerName}! \n\nTe acabo de enviar nuestro catálogo completo. Algunos de los servicios más solicitados son:\n{serviceList}{extra}\n\n👉 *Por favor, escríbeme qué servicio deseas reservar.*";
        }

        var offerings = await _offeringService.SearchOfferingsAsync(workspaceId, null, "SERVICE", context.SelectedServiceId, ct);
        var targetService = offerings.FirstOrDefault();
        if (targetService == null) return $"Disculpa, no logré encontrar el servicio '{context.SelectedServiceId}'. ¿Podrías escribirlo de nuevo?";

        // PASO 2: Sede
        if (string.IsNullOrWhiteSpace(context.SelectedLocationId))
        {
            var locations = await _locationRepo.GetLocationsAsync(workspaceId, ct);
            var locationList = string.Join("\n", locations.Select(l => $"- {l.Name}"));
            return $"Has elegido *{targetService.Name}*.\n\n¿En cuál de nuestras sedes te gustaría atenderte?\n{locationList}\n\n👉 *Escribe tu sede preferida.*";
        }

        bool isAvailable = await _offeringService.IsAvailableAtLocationAsync(workspaceId, targetService.Id, context.SelectedLocationId, ct);
        if (!isAvailable) return $"El servicio de {targetService.Name} no está disponible en esa sede. ¿Te gustaría intentar en otra?";

        // PASO 3: Fecha
        if (string.IsNullOrWhiteSpace(context.TargetDate))
            return $"¡Excelente! 🏥\n\n¿Para qué fecha te gustaría programar tu cita?\n👉 *(Ej: 'mañana', 'el próximo viernes', o 'el 25 de octubre').*";

        if (!DateTime.TryParse(context.TargetDate, out var parsedDate)) return "No logré entender la fecha. ¿Podrías decírmela en formato YYYY-MM-DD o 'mañana'?";

        // PASO 4: Hora (Con Horario de Atención Real)
        if (string.IsNullOrWhiteSpace(context.TargetTime))
        {
            var hours = await _hoursRepo.GetBusinessHoursAsync(workspaceId, context.SelectedLocationId, ct);
            var dayHours = hours.FirstOrDefault(h => h.DayOfWeek == (int)parsedDate.DayOfWeek);

            if (dayHours == null || dayHours.IsClosed)
                return $"Ese día nos encontramos cerrados. ¿Te gustaría intentar con otra fecha?";

            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, context.SelectedLocationId, targetService.Id, parsedDate, ct);
            if (!slots.Any()) return $"Lo lamento mucho, pero tenemos la agenda llena el {parsedDate:dd/MM/yyyy}. ¿Intentamos otro día?";

            return $"Para el *{parsedDate:dd/MM/yyyy}*, nuestro horario de atención es de *{dayHours.OpenTime} a {dayHours.CloseTime}*.\n\n👉 *Tenemos turnos disponibles, ¿A qué hora prefieres que te agendemos? (Ej: 'a las 10:00 am')*";
        }

        // PASO 5: Confirmación
        var rawDateTime = $"{context.TargetDate} {context.TargetTime}";
        if (DateTime.TryParse(rawDateTime, out var exactDateTime))
        {
            var result = await _reservationEngine.CreateReservationAsync(workspaceId, context.SelectedLocationId, targetService.Id, phone, context.RealCustomerName, exactDateTime, ct);
            if (result.IsSuccess)
            {
                context.CurrentGoal = null;
                return $"✅ *¡Todo listo, {context.RealCustomerName}!*\n\nTu cita ha sido confirmada exitosamente:\n🦷 Servicio: *{targetService.Name}*\n📅 Fecha: *{exactDateTime:dd/MM/yyyy}*\n⏰ Hora: *{exactDateTime:HH:mm}*\n\n¡Te esperamos! Si necesitas cancelar o reagendar, solo dímelo.";
            }
            context.TargetTime = null; // Borramos la hora para pedirla de nuevo
            return $"Tuvimos un inconveniente: {result.Error.Description}. Por favor, indícame otro horario.";
        }

        return "Hubo un error al procesar el horario. ¿Podrías indicarme la hora nuevamente?";
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
    public ChatFlow(IAiRouter aiRouter) => _aiRouter = aiRouter;

    public async Task<string> ProcessAsync(string text, CancellationToken ct)
    {
        return await _aiRouter.ExecuteTaskAsync(AiTaskType.ComplexChat, "Eres un asistente amable. Responde con cortesía y guía al cliente.", text, false, ct);
    }
}