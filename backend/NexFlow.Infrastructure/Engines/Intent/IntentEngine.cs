using System.Text.Json;
using NexFlow.Application.DTOs.AI;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;

namespace NexFlow.Infrastructure.Engines.Intent;

public class IntentEngine : IIntentEngine
{
    private readonly IAiProvider _aiProvider;

    public IntentEngine(IAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public async Task<IntentResultDto> AnalyzeAsync(string message, CancellationToken cancellationToken)
    {
        var systemPrompt = @"
Eres el motor de clasificación de NexFlow. Tu único objetivo es analizar el mensaje del usuario y devolver un JSON estricto, sin markdown ni explicaciones adicionales.
Debes clasificar la intención en una de estas opciones: 'CREATE_RESERVATION', 'CHECK_AVAILABILITY', 'CANCEL_RESERVATION', 'FAQ', 'GENERAL_GREETING', 'UNKNOWN'.

Formato de respuesta obligatorio:
{
    ""Intent"": ""STRING"",
    ""Confidence"": 0.95,
    ""Parameters"": { ""clave"": ""valor"" }
}

Reglas para los parámetros:
- Si es una reserva, extrae 'service' (ej. Corte), 'date' (ej. 2026-08-20), y 'time' (ej. 15:00) si están presentes.
- Si no hay parámetros relevantes, devuelve un objeto vacío {}.
";

        try
        {
            var jsonResponse = await _aiProvider.GenerateTextAsync(systemPrompt, message, cancellationToken);

            // Limpiamos la respuesta por si Gemini devuelve ```json ... ```
            jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();

            var result = JsonSerializer.Deserialize<IntentResultDto>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result ?? new IntentResultDto("UNKNOWN", 0, new Dictionary<string, string>());
        }
        catch
        {
            // Si la IA alucina o el JSON falla, no rompemos el sistema, lo mandamos a un fallback seguro
            return new IntentResultDto("UNKNOWN", 0, new Dictionary<string, string>());
        }
    }
}