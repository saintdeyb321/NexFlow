using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Cache;
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

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark && !char.IsPunctuation(c))
            {
                sb.Append(c);
            }
        }

        var cleanStr = sb.ToString().Trim();
        cleanStr = Regex.Replace(cleanStr, @"([a-z])\1+", "$1");

        return cleanStr;
    }

    private IntentResultDto? EvaluateFastIntent(string normMsg, int wordCount)
    {
        if (wordCount <= 3 && Regex.IsMatch(normMsg, @"\b(hola|ola|buenas|bns|bueno dia|buena tarde|buena noche|hey|saludo)\b"))
            return new IntentResultDto(IntentType.GeneralGreeting, 1.0, new Dictionary<string, string>());

        if (wordCount <= 2 && Regex.IsMatch(normMsg, @"^(gracia|ok|perfecto|entendido|vale|listo|si|no|sip|nop|dale|ya)$"))
            return new IntentResultDto(IntentType.Unknown, 1.0, new Dictionary<string, string>());

        if (Regex.IsMatch(normMsg, @"\b(asesor|humano|persona|queja|reclamo|representante|hablar)\b"))
            return new IntentResultDto(IntentType.HumanHandoffRequest, 0.9, new Dictionary<string, string>());

        if (wordCount <= 6)
        {
            if (Regex.IsMatch(normMsg, @"\b(donde|ubicacion|direccion|sede|legar)\b"))
                return new IntentResultDto(IntentType.LocationQuery, 0.9, new Dictionary<string, string>());

            if (Regex.IsMatch(normMsg, @"\b(horario|hora|atienden|abren|cieran)\b"))
                return new IntentResultDto(IntentType.BusinessHoursQuery, 0.9, new Dictionary<string, string>());

            if (Regex.IsMatch(normMsg, @"\b(precio|costo|cuanto|catalogo|servicio|serbicio)\b"))
                return new IntentResultDto(IntentType.ServiceInformation, 0.85, new Dictionary<string, string>());
        }

        return null;
    }

    public async Task<IntentResultDto> AnalyzeAsync(string message, ConversationContextDto? context, CancellationToken cancellationToken)
    {
        var normMsg = NormalizeText(message);
        var wordCount = normMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        var fastIntent = EvaluateFastIntent(normMsg, wordCount);
        if (fastIntent != null)
        {
            _logger.LogInformation("Fast Intent resuelto localmente: {Intent} para el mensaje '{Message}'", fastIntent.Intent, message);
            return fastIntent;
        }

        // 🔥 Auditoría (Sprint 2.2): Formateamos la memoria del chat para que la IA sepa de qué hablamos.
        string contextInfo = "Ninguno (Nueva conversación).";
        if (context != null && (!string.IsNullOrEmpty(context.CurrentIntent) || !string.IsNullOrEmpty(context.PendingAction)))
        {
            contextInfo = $"Intención Actual: {context.CurrentIntent ?? "Ninguna"}. Acción Pendiente: {context.PendingAction ?? "Ninguna"}.";
        }

        // Se utilizan dobles llaves {{ }} para escapar el JSON dentro de una cadena interpolada ($"")
        var systemPrompt = $@"Clasifica el mensaje en UNA de estas intenciones exactas:
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

CONTEXTO DE LA CONVERSACIÓN ACTUAL:
{contextInfo}
(Usa este contexto para desambiguar. Ejemplo: si dicen 'mañana' y el contexto es 'CreateReservation', la intención es CreateReservation).

Devuelve ÚNICAMENTE un JSON exacto: {{ ""Intent"": """", ""Confidence"": 0.0, ""Parameters"": {{}} }}";

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
            _logger.LogError(ex, "Fallo crítico al clasificar la intención del mensaje mediante IA: {Message}", message);
            return new IntentResultDto(IntentType.ProviderUnavailable, 0, new Dictionary<string, string>());
        }
    }

    private record RawIntentDto(string Intent, double Confidence, Dictionary<string, string> Parameters);
}