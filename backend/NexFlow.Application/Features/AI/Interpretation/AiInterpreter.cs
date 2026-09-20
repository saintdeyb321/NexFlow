using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Router;

namespace NexFlow.Application.Features.AI.Interpretation;

public class AiInterpretation
{
    public string Intent { get; set; } = "CHAT";
    public string? Service { get; set; }
    public string? Location { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    // 🔥 UX FIX: El bot ahora debe saber buscar el nombre real de la persona
    public string? CustomerName { get; set; }
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
(CRÍTICO: Si el negocio NO tiene el módulo 'RESERVATIONS', no puedes devolver el Intent 'BOOKING'. Si no tiene 'REQUESTS', no devuelvas 'REQUEST').

FECHA ACTUAL: {localBusinessTime:yyyy-MM-dd}
HORA ACTUAL: {localBusinessTime:HH:mm}
OBJETIVO ACTUAL: {(string.IsNullOrWhiteSpace(currentGoal) ? "NINGUNO" : currentGoal)}

- Intent: 'BOOKING', 'INFO', 'REQUEST', 'SUPPORT' o 'CHAT'.
- Service: Nombre del servicio (Solo si Intent es BOOKING).
- Location: La sede mencionada.
- Date: Fecha en formato YYYY-MM-DD.
- Time: Hora en formato HH:mm.
- CustomerName: Nombre y apellido del cliente (Solo si lo menciona explícitamente).

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
            _logger.LogError(ex, "Error crítico en AI Interpreter después del Fallback. Asumiendo intención CHAT.");
            return new AiInterpretation();
        }
    }
}