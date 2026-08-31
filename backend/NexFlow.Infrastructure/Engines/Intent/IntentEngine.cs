using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Infrastructure.Engines.Intent;

public class IntentEngine : IIntentEngine
{
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<IntentEngine> _logger;

    public IntentEngine(IAiProvider aiProvider, ILogger<IntentEngine> logger)
    {
        _aiProvider = aiProvider;
        _logger = logger;
    }

    public async Task<IntentResultDto> AnalyzeAsync(string message, CancellationToken cancellationToken)
    {
        var systemPrompt = @"
Eres el motor de clasificación (Intent Engine) de NexFlow. Tu tarea es clasificar estrictamente el mensaje en una de las siguientes intenciones.

REGLA MULTI-TURNO OBLIGATORIA: Si el usuario responde con un dato suelto (ej: 'El Tambo', 'Mañana', 'Corte de cabello'), ASUME que está continuando un flujo. Clasifícalo como CreateReservation o ServiceInformation y extrae el dato en Parameters. NUNCA lo clasifiques como Unknown.

- CreateReservation: Quiere agendar.
- CheckAvailability: Pregunta por disponibilidad.
- CancelReservation: Anular reserva.
- CreateRequest: Trámite o soporte.
- CheckRequestStatus: Estado de trámite.
- ServiceInformation: Servicios, precios o características. NUNCA uses FaqQuery para esto.
- ProductInformation: Catálogo o productos físicos.
- FaqQuery: Preguntas operativas generales (pagos, requisitos).
- BusinessProfileQuery: Quiénes son, historia, ruc.
- LocationQuery: Dónde están, direcciones.
- BusinessHoursQuery: A qué hora abren/cierran.
- HumanHandoffRequest: Cliente enojado, pide un humano, O ENVIÓ MULTIMEDIA ('[Mensaje de Audio]', '[Mensaje de Imagen]', '[Documento Adjunto]').
- GeneralGreeting: Saludos simples.
- Unknown: Totalmente incomprensible.

Devuelve UNICAMENTE un JSON con: Intent, Confidence y Parameters.";

        try
        {
            var jsonResponse = await _aiProvider.GenerateTextAsync(systemPrompt, message, useJsonMode: true, cancellationToken);
            jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();

            var rawResult = JsonSerializer.Deserialize<RawIntentDto>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (rawResult != null && Enum.TryParse<IntentType>(rawResult.Intent, true, out var intentType))
            {
                return new IntentResultDto(intentType, rawResult.Confidence, rawResult.Parameters ?? new Dictionary<string, string>());
            }

            return new IntentResultDto(IntentType.Unknown, 0, new Dictionary<string, string>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo crítico al clasificar la intención del mensaje: {Message}", message);
            return new IntentResultDto(IntentType.Unknown, 0, new Dictionary<string, string>());
        }
    }

    private record RawIntentDto(string Intent, double Confidence, Dictionary<string, string> Parameters);
}