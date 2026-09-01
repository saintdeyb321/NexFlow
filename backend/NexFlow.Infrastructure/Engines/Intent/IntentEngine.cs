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
        // 🔥 Auditoría: FAST INTENT LAYER. Cero llamadas a IA para interacciones básicas.
        var lowerMsg = message.Trim().ToLowerInvariant();
        var greetings = new[] { "hola", "buenas", "buenos dias", "buenas tardes", "buenas noches", "hey", "saludos" };
        var acknowledgments = new[] { "gracias", "ok", "perfecto", "entendido", "vale", "listo", "si", "no" };

        if (greetings.Contains(lowerMsg))
            return new IntentResultDto(IntentType.GeneralGreeting, 1.0, new Dictionary<string, string>());

        if (acknowledgments.Contains(lowerMsg))
            return new IntentResultDto(IntentType.Unknown, 1.0, new Dictionary<string, string>());

        // 🔥 Auditoría: Prompt hiper-compactado para reducir la latencia de carga en Gemini.
        var systemPrompt = @"Clasifica el mensaje en UNA de estas intenciones exactas:
- CreateReservation, CheckAvailability, CancelReservation
- CreateRequest, CheckRequestStatus
- ServiceInformation (Precios/servicios)
- ProductInformation (Catálogo)
- FaqQuery (Pagos, reglas operativas)
- BusinessProfileQuery (Quiénes son)
- LocationQuery (Dónde están, sedes)
- BusinessHoursQuery (Horarios)
- HumanHandoffRequest (Quejas, exigir humano o envío de multimedia)
- GeneralGreeting
- Unknown (Datos sueltos o incomprensibles)

Devuelve ÚNICAMENTE un JSON exacto: { ""Intent"": """", ""Confidence"": 0.0, ""Parameters"": {} }";

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