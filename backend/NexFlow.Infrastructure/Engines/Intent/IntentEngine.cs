using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
Eres el clasificador de NexFlow. Analiza el mensaje y devuelve un JSON estricto.
Opciones de Intent (usa estos textos exactos): CreateReservation, CheckAvailability, CancelReservation, Faq, GeneralGreeting, Unknown.

Formato:
{
    ""Intent"": ""CreateReservation"",
    ""Confidence"": 0.95,
    ""Parameters"": { ""service"": ""corte"" }
}";
        try
        {
            var jsonResponse = await _aiProvider.GenerateTextAsync(systemPrompt, message, cancellationToken);
            jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();

            var rawResult = JsonSerializer.Deserialize<RawIntentDto>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (rawResult != null && Enum.TryParse<IntentType>(rawResult.Intent, true, out var intentType))
            {
                return new IntentResultDto(intentType, rawResult.Confidence, rawResult.Parameters ?? new());
            }

            _logger.LogWarning("La IA devolvió un Intent no mapeable: {RawIntent}", rawResult?.Intent);
            return new IntentResultDto(IntentType.Unknown, 0, new Dictionary<string, string>());
        }
        catch (Exception ex)
        {
            // Observabilidad: Registramos el error sin romper la aplicación
            _logger.LogError(ex, "Fallo crítico al clasificar la intención del mensaje: {Message}", message);
            return new IntentResultDto(IntentType.Unknown, 0, new Dictionary<string, string>());
        }
    }

    // Clase privada para deserializar el string crudo de la IA antes del casteo seguro
    private record RawIntentDto(string Intent, double Confidence, Dictionary<string, string> Parameters);
}