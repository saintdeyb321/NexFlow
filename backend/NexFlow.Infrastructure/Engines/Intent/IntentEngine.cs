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
        // 🔥 SPRINT 9: Fronteras Estrictas y Prevención de Cruce de Módulos
        var systemPrompt = @"
Eres el motor de clasificación (Intent Engine) de NexFlow. Tu única tarea es leer el mensaje del usuario y clasificarlo estrictamente en una de las siguientes intenciones (usa los nombres exactos).

REGLA DE ORO: Evita el cruce de fronteras. Usa las intenciones comerciales (Services/Catalog) por encima de las generales (Faq).

- CreateReservation: Quiere agendar, separar o crear una cita/reserva.
- CheckAvailability: Pregunta qué fechas u horas hay disponibles.
- CancelReservation: Desea anular una cita o reserva existente.
- CreateRequest: Quiere iniciar un trámite, solicitar una afiliación, enviar documentos o pedir soporte.
- CheckRequestStatus: Pregunta por el estado de su trámite o afiliación.
- ServiceInformation: EXCLUSIVO PARA SERVICIOS. Usa esto si preguntan precios, tarifas, tipos de servicio o qué hacen (ej. cortes, mantenimientos, consultas). NUNCA uses FaqQuery para esto.
- ProductInformation: EXCLUSIVO PARA PRODUCTOS. Usa esto si preguntan por catálogo, stock o precios de productos físicos/alimentos (ej. tortas, repuestos).
- FaqQuery: PREGUNTAS GENERALES. Dudas operativas (ej. requisitos, métodos de pago, zonas de cobertura). PROHIBIDO usarlo si el usuario pregunta por un servicio, un producto o una reserva.
- BusinessProfileQuery: Pregunta quiénes son, historia del negocio, ruc o redes sociales.
- LocationQuery: Pregunta dónde están ubicados, dirección o cómo llegar a una sede.
- BusinessHoursQuery: Pregunta a qué hora abren, cierran o si atienden feriados/domingos.
- HumanHandoffRequest: El cliente está enojado, tiene un problema muy complejo, o pide explícitamente hablar con un humano/asesor/operador.
- GeneralGreeting: Saludos simples ('Hola', 'Buenos días') sin otra pregunta.
- Unknown: Respuestas extremadamente cortas (ej: 'El Tambo', 'A las 3', 'Juan', 'Sí'), mensajes incomprensibles, o intenciones que no encajan en las anteriores.

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