using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.AI.Interpretation;

public class AiInterpretation
{
    // 🔥 SPRINT 05: Fuertemente tipado
    public ConversationIntent Intent { get; set; } = ConversationIntent.General;
    public string? Service { get; set; }
    public string? Location { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public string? CustomerName { get; set; }
    public string? SearchTerm { get; set; }
}

public interface IAiInterpreter
{
    Task<AiInterpretation> InterpretAsync(Guid workspaceId, string userMessage, string currentGoal, HashSet<string> activeModules, CancellationToken ct);
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

    public async Task<AiInterpretation> InterpretAsync(Guid workspaceId, string userMessage, string currentGoal, HashSet<string> activeModules, CancellationToken ct)
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
(CRÍTICO: Si el negocio NO tiene 'RESERVATIONS', no puedes devolver 'Reservation'. Si no tiene 'ORDERS', no devuelvas 'Order').

FECHA ACTUAL: {localBusinessTime:yyyy-MM-dd}
HORA ACTUAL: {localBusinessTime:HH:mm}
OBJETIVO ACTUAL: {(string.IsNullOrWhiteSpace(currentGoal) ? "NINGUNO" : currentGoal)}

- Intent: 'ProductQuery', 'ServiceQuery', 'Reservation', 'Request', 'Order', 'Faq', 'Location', 'General', 'Support', 'BusinessHours'.
- SearchTerm: Si el cliente hace un PEDIDO (Order), extrae los productos y SUS CANTIDADES (Ej: '2x martillos', '1x clavo'). Si el cliente indica que ya terminó de pedir, o dice 'enviar pedido', pon EXACTAMENTE: 'FINALIZAR_PEDIDO'.
- Service: Nombre del servicio (Solo si Intent es Reservation).
- Location: La sede mencionada.
- Date: Fecha en formato YYYY-MM-DD.
- Time: Hora en formato HH:mm.
- CustomerName: Nombre y apellido del cliente.

RESPONDE ÚNICAMENTE CON EL JSON. Asegúrate de que el campo Intent coincida exactamente con las opciones dadas.";

        try
        {
            var responseText = await _router.ExecuteTaskAsync(AiTaskType.IntentExtraction, prompt, userMessage, true, ct);
            var cleanJson = responseText.Replace("```json", "").Replace("```", "").Trim();

            var interpretation = JsonSerializer.Deserialize<AiInterpretation>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new AiInterpretation { Intent = ConversationIntent.General };

            return interpretation;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
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