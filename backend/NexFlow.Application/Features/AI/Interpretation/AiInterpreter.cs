using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Router;

namespace NexFlow.Application.Features.AI.Interpretation;

public class AiInterpretation
{
    public string Intent { get; set; } = "GENERAL";
    public string? Service { get; set; }
    public string? Location { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public string? CustomerName { get; set; }
    // 🔥 SPRINT 03: Para buscar productos, servicios o FAQs específicos sin cargar todo
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
(CRÍTICO: Si el negocio NO tiene 'RESERVATIONS', no puedes devolver 'RESERVATION'. Si no tiene 'CATALOG', no devuelvas 'PRODUCT_QUERY').

FECHA ACTUAL: {localBusinessTime:yyyy-MM-dd}
HORA ACTUAL: {localBusinessTime:HH:mm}
OBJETIVO ACTUAL: {(string.IsNullOrWhiteSpace(currentGoal) ? "NINGUNO" : currentGoal)}

- Intent: 'PRODUCT_QUERY', 'SERVICE_QUERY', 'RESERVATION', 'REQUEST', 'FAQ', 'LOCATION', 'GENERAL'.
- SearchTerm: Si el cliente pregunta por un producto, servicio o duda concreta, extrae las palabras clave de búsqueda aquí.
- Service: Nombre del servicio (Solo si Intent es RESERVATION).
- Location: La sede mencionada.
- Date: Fecha en formato YYYY-MM-DD.
- Time: Hora en formato HH:mm.
- CustomerName: Nombre y apellido del cliente.

RESPONDE ÚNICAMENTE CON EL JSON.";

        try
        {
            var responseText = await _router.ExecuteTaskAsync(AiTaskType.IntentExtraction, prompt, userMessage, true, ct);
            var cleanJson = responseText.Replace("```json", "").Replace("```", "").Trim();

            return JsonSerializer.Deserialize<AiInterpretation>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new AiInterpretation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico en AI Interpreter. Asumiendo intención GENERAL.");
            return new AiInterpretation();
        }
    }
}