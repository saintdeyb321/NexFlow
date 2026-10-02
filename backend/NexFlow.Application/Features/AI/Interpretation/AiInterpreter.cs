using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.AI.Interpretation;

public enum ConversationDirective { None, ChangeLocation, ChangeService, ShowOptions, CancelCurrentFlow }
public enum OfferingQueryKind { Specific, Broad }

public class AiInterpretation
{
    // 🔥 SPRINT 05: Fuertemente tipado
    [JsonRequired]
    public ConversationIntent Intent { get; set; } = ConversationIntent.General;
    [JsonIgnore]
    public string? DeniedResponse { get; set; }
    public string? Service { get; set; }
    public string? Location { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public string? CustomerName { get; set; }
    public string? SearchTerm { get; set; }
    public ConversationDirective Directive { get; set; }
    public OfferingQueryKind QueryKind { get; set; } = OfferingQueryKind.Specific;
}

public interface IAiInterpreter
{
    Task<AiInterpretation> InterpretAsync(Guid workspaceId, string userMessage, string currentGoal, string? currentStep, HashSet<string> activeModules, CancellationToken ct);
}

public class AiInterpreter : IAiInterpreter
{
    private readonly IAiRouter _router;
    private readonly IClock _clock;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly ILogger<AiInterpreter> _logger;

    public AiInterpreter(IAiRouter router, IClock clock, IBusinessProfileRepository profileRepo, ILogger<AiInterpreter> logger)
    {
        _router = router;
        _clock = clock;
        _profileRepo = profileRepo;
        _logger = logger;
    }

    public async Task<AiInterpretation> InterpretAsync(Guid workspaceId, string userMessage, string currentGoal, string? currentStep, HashSet<string> activeModules, CancellationToken ct)
    {
        var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
        var tzId = string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone;

        TimeZoneInfo workspaceZone;
        try { workspaceZone = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { workspaceZone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }

        var localBusinessTime = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, workspaceZone);
        var activeModulesList = string.Join(", ", activeModules);

        var prompt = $@"Eres el Intérprete Lingüístico de un sistema transaccional.
Tu ÚNICO trabajo es extraer intenciones y entidades en JSON.
MÓDULOS PAGADOS POR ESTE NEGOCIO: {activeModulesList}
Identifica la intención real aunque el módulo no esté habilitado; el software validará el acceso.

FECHA ACTUAL: {localBusinessTime:yyyy-MM-dd}
HORA ACTUAL: {localBusinessTime:HH:mm}
OBJETIVO ACTUAL: {(string.IsNullOrWhiteSpace(currentGoal) ? "NINGUNO" : currentGoal)}
PASO ACTUAL: {currentStep ?? "NINGUNO"}

- Intent: 'ProductQuery', 'ServiceQuery', 'Reservation', 'Request', 'Order', 'Faq', 'Location', 'General', 'Support', 'BusinessHours'.
- SearchTerm: Si el cliente hace un PEDIDO (Order), extrae los productos y SUS CANTIDADES (Ej: '2x martillos', '1x clavo'). Si el cliente indica que ya terminó de pedir, o dice 'enviar pedido', pon EXACTAMENTE: 'FINALIZAR_PEDIDO'.
- SearchTerm: Para ProductQuery, ServiceQuery o Faq, extrae sólo el producto, servicio o tema consultado; no la pregunta completa.
- Service: Nombre del servicio mencionado para reservar o cambiar el servicio del draft.
- Location: La sede mencionada.
- Date: Fecha en formato YYYY-MM-DD.
- Time: Hora en formato HH:mm.
- CustomerName: Nombre y apellido del cliente.
- Directive: 'None', 'ChangeLocation', 'ChangeService', 'ShowOptions', 'CancelCurrentFlow'. Extrae sólo comandos explícitos del consumidor.
- QueryKind: 'Broad' para consultas amplias de productos/servicios ('qué productos tienen', 'qué servicios ofrecen', 'muéstrame el catálogo', 'pásame sus productos', 'lista de servicios' y equivalentes). SearchTerm = null en estas consultas.
- QueryKind: 'Specific' si pregunta sobre una offering concreta, su precio, duración o existencia; SearchTerm contiene únicamente su nombre. No conviertas una consulta específica en Broad.

El objetivo activo NO reemplaza la intención de una pregunta informativa. Si pregunta explícitamente horarios, Intent = BusinessHours; si pregunta ubicación o dirección, Intent = Location; si pregunta un dato frecuente, Intent = Faq. En estas interrupciones no extraigas cambios del draft ni nombres, fechas u horas como respuestas al paso actual.
Si dice 'quiero cambiar de sede', 'puedo cambiar de sede', 'otra sede': Directive = ChangeLocation. No lo confundas con una consulta de dirección.
Si pide cambiar el servicio: Directive = ChangeService.
Si pregunta 'no tiene más servicios?', 'qué otros servicios hay?', 'qué servicios tienen aquí?' durante RESERVATION/BOOKING: Intent = ServiceQuery, Directive = ShowOptions, QueryKind = Broad.
Si responde 'ver más', 'más opciones' o 'otros servicios' en la selección de servicio: Directive = ShowOptions. Si pide el catálogo, conserva ProductQuery/ServiceQuery con QueryKind = Broad y Directive = None para consultar el artefacto.
CancelCurrentFlow sólo cancela el draft actual por petición explícita; no significa cancelar una reserva ya registrada.
Durante RESERVATION/BOOKING y COLLECT_SERVICE, 'cambio de transmision', 'quiero el cambio de transmision', 'el de transmision', 'vale quiero X' son selección de servicio: Intent = Reservation y Service = el servicio mencionado, aunque no diga reservar.
Durante COLLECT_CUSTOMERNAME extrae el nombre escrito como respuesta; durante COLLECT_LOCATION extrae la sede escrita; durante COLLECT_DATE/COLLECT_TIME extrae fecha/hora sólo si el mensaje responde al paso.
Durante RESERVATION/BOOKING y COLLECT_LOCATION, el nombre de una sede como respuesta tiene Intent = Reservation y Location = la sede escrita. Sólo una pregunta explícita sobre ubicación/dirección tiene Intent = Location. Una respuesta de fecha/hora al paso también tiene Intent = Reservation; no es una consulta BusinessHours.
No inventes entidades, datos comerciales, sedes, servicios, precios, horarios o disponibilidad. No decidas opciones válidas ni transiciones: todo ello lo valida software determinista.

RESPONDE ÚNICAMENTE CON EL JSON. Asegúrate de que el campo Intent coincida exactamente con las opciones dadas.";

        try
        {
            var responseText = await _router.ExecuteTaskAsync(AiTaskType.IntentExtraction, prompt, userMessage, true, ct);
            var cleanJson = responseText.Replace("```json", "").Replace("```", "").Trim();

            var interpretation = JsonSerializer.Deserialize<AiInterpretation>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } })
                   ?? throw new JsonException("Interpretation was null.");

            if (!Enum.IsDefined(interpretation.Intent) || !Enum.IsDefined(interpretation.Directive) || !Enum.IsDefined(interpretation.QueryKind)
                || interpretation.Intent is ConversationIntent.ProviderUnavailable or ConversationIntent.RateLimited)
                throw new JsonException("Invalid model intent.");
            interpretation.DeniedResponse = AiIntentAccess.GetDeniedResponse(interpretation.Intent, activeModules);
            return interpretation;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(ex, "Rate Limit alcanzado en el proveedor de IA.");
            return new AiInterpretation { Intent = ConversationIntent.RateLimited };
        }
        catch (Exception ex)
        {
            // 🔥 SPRINT 05: Ya no ocultamos el error detrás de "GENERAL"
            _logger.LogError(ex, "Error crítico en IA. Proveedor inalcanzable o timeout.");
            return new AiInterpretation { Intent = ConversationIntent.ProviderUnavailable };
        }
    }
}

internal static class AiIntentAccess
{
    internal const string ProductsUnavailable = "No hay información de productos disponible en este momento.";
    internal const string ServicesUnavailable = "No hay información de servicios disponible en este momento.";

    internal static string? GetDeniedResponse(ConversationIntent intent, ISet<string> activeModules)
    {
        bool Has(string module) => activeModules.Any(m => string.Equals(m, module, StringComparison.OrdinalIgnoreCase));
        return intent switch
        {
            ConversationIntent.ProductQuery when !Has("CATALOG") => ProductsUnavailable,
            ConversationIntent.ServiceQuery when !Has("SERVICES") => ServicesUnavailable,
            ConversationIntent.Reservation when !Has("RESERVATIONS") => "Este negocio no tiene habilitadas las reservas.",
            ConversationIntent.Reservation when !Has("SERVICES") => ServicesUnavailable,
            ConversationIntent.Order when !Has("ORDERS") => "Este negocio no tiene habilitados los pedidos.",
            ConversationIntent.Order when !Has("CATALOG") => ProductsUnavailable,
            ConversationIntent.Request when !Has("REQUESTS") => "Este negocio no tiene habilitado el registro de solicitudes.",
            _ => null
        };
    }
}
