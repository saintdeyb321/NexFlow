using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Features.AI.Router;

namespace NexFlow.Application.Features.AI.Interpretation;

public class AiInterpretation
{
    public string Intent { get; set; } = "CHAT";
    public string? Service { get; set; }
    public string? Location { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
}

public interface IAiInterpreter
{
    Task<AiInterpretation> InterpretAsync(string userMessage, string currentGoal, HashSet<string> activeModules, CancellationToken ct);
}

public class AiInterpreter : IAiInterpreter
{
    private readonly IAiRouter _router;
    private readonly ILogger<AiInterpreter> _logger;

    public AiInterpreter(IAiRouter router, ILogger<AiInterpreter> logger)
    {
        _router = router;
        _logger = logger;
    }

    public async Task<AiInterpretation> InterpretAsync(string userMessage, string currentGoal, HashSet<string> activeModules, CancellationToken ct)
    {
        var activeModulesList = string.Join(", ", activeModules);
        var prompt = $@"Eres el Intérprete Lingüístico de un sistema transaccional.
Tu ÚNICO trabajo es extraer intenciones y entidades en JSON.
MÓDULOS PAGADOS POR ESTE NEGOCIO: {activeModulesList}
(CRÍTICO: Si el negocio NO tiene el módulo 'RESERVATIONS', no puedes devolver el Intent 'BOOKING'. Si no tiene 'REQUESTS', no devuelvas 'REQUEST').

FECHA ACTUAL: {DateTime.Now:yyyy-MM-dd}
OBJETIVO ACTUAL: {(string.IsNullOrWhiteSpace(currentGoal) ? "NINGUNO" : currentGoal)}

- Intent: 'BOOKING', 'INFO', 'REQUEST', 'SUPPORT' o 'CHAT'.
- Service: Nombre del servicio (Solo si Intent es BOOKING).
- Location: La sede mencionada.
- Date: Fecha en formato YYYY-MM-DD.
- Time: Hora en formato HH:mm.

RESPONDE ÚNICAMENTE CON EL JSON.";

        try
        {
            var provider = _router.GetProvider(AiTaskType.IntentExtraction);
            var responseText = await provider.GenerateTextAsync(prompt, userMessage, useJsonMode: true, ct);
            var cleanJson = responseText.Replace("```json", "").Replace("```", "").Trim();

            return JsonSerializer.Deserialize<AiInterpretation>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new AiInterpretation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en AI Interpreter (Groq Falló). Fallback a CHAT.");
            return new AiInterpretation();
        }
    }
}