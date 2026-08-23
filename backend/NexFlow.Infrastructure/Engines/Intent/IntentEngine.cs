using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
Eres el motor de clasificación (Intent Engine) de NexFlow. Tu única tarea es leer el mensaje del usuario y clasificarlo estrictamente en una de las siguientes intenciones (usa los nombres exactos):

- CreateReservation: Quiere agendar, separar o crear una cita/reserva.
- CheckAvailability: Pregunta qué fechas u horas hay disponibles.
- CancelReservation: Desea anular una cita existente.
- CreateRequest: Quiere iniciar un trámite, solicitar una afiliación, enviar documentos o pedir soporte.
- CheckRequestStatus: Pregunta por el estado de su trámite o afiliación.
- ServiceInformation: Pregunta qué servicios brindan, en qué consisten o cuánto cuestan (ej. cortes, consultas, mantenimientos).
- ProductInformation: Pregunta por el catálogo de productos físicos o alimentos (ej. tortas, repuestos).
- FaqQuery: Dudas generales o preguntas frecuentes (ej. requisitos, métodos de pago, si hay delivery).
- BusinessProfileQuery: Pregunta quiénes son, historia del negocio o redes sociales.
- LocationQuery: Pregunta dónde están ubicados, dirección o cómo llegar.
- BusinessHoursQuery: Pregunta a qué hora abren, cierran o si atienden feriados/domingos.
- HumanHandoffRequest: El cliente está enojado, tiene un problema muy complejo, o pide explícitamente hablar con un humano/asesor.
- GeneralGreeting: Saludos simples ('Hola', 'Buenos días') sin otra pregunta.
- Unknown: Mensajes incomprensibles, insultos, o intenciones que no encajan en las anteriores.

Devuelve UNICAMENTE un JSON válido con este formato exacto:
{
    ""Intent"": ""ProductInformation"",
    ""Confidence"": 0.95,
    ""Parameters"": { ""item"": ""torta"" }
}";
        try
        {
            var jsonResponse = await _aiProvider.GenerateTextAsync(systemPrompt, message, useJsonMode: true, cancellationToken);
            jsonResponse = jsonResponse.Replace("```json", "").Replace("```", "").Trim();

            var rawResult = JsonSerializer.Deserialize<RawIntentDto>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (rawResult != null && Enum.TryParse<IntentType>(rawResult.Intent, true, out var intentType))
            {
                return new IntentResultDto(intentType, rawResult.Confidence, rawResult.Parameters ?? new Dictionary<string, string>());
            }

            _logger.LogWarning("La IA devolvió un Intent no mapeable: {RawIntent}", rawResult?.Intent);
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