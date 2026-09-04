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
            if (Regex.IsMatch(normMsg, @"\b(donde|ubicacion|direccion|sede|llegar)\b"))
                return new IntentResultDto(IntentType.LocationQuery, 0.9, new Dictionary<string, string>());

            if (Regex.IsMatch(normMsg, @"\b(horario|hora|atienden|abren|cierran)\b"))
                return new IntentResultDto(IntentType.BusinessHoursQuery, 0.9, new Dictionary<string, string>());

            // 🔥 SPRINT 1.2: Aislamiento determinista entre Servicios y Catálogo
            // Se eliminan palabras ambiguas como "precio", "costo", "cuanto" para que Gemini las evalúe con el contexto.
            if (Regex.IsMatch(normMsg, @"\b(catalogo|producto|productos)\b") && !Regex.IsMatch(normMsg, @"\b(servicio|servicios)\b"))
                return new IntentResultDto(IntentType.ProductInformation, 0.85, new Dictionary<string, string>());

            if (Regex.IsMatch(normMsg, @"\b(servicio|servicios|serbicio|serbicios)\b") && !Regex.IsMatch(normMsg, @"\b(catalogo|producto)\b"))
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
            _logger.LogInformation("Fast Intent resuelto localmente: {Intent}", fastIntent.Intent);
            return fastIntent;
        }

        // 🔥 SPRINT 1.2: Memoria Estructurada Completa para Gemini (Evita que pierda el hilo)
        string contextInfo = "Ninguno (Nueva conversación).";
        if (context != null && (!string.IsNullOrEmpty(context.CurrentIntent) || !string.IsNullOrEmpty(context.PendingAction)))
        {
            contextInfo = $@"
- Intención Actual: {context.CurrentIntent ?? "Ninguna"}
- Acción Pendiente: {context.PendingAction ?? "Ninguna"}
- Sede Seleccionada: {context.SelectedLocationId ?? "Ninguna"}
- Servicio Seleccionado: {context.SelectedServiceId ?? "Ninguno"}";
        }

        var systemPrompt = $@"Clasifica el mensaje del usuario en UNA de estas intenciones exactas:
- CreateReservation, CheckAvailability, CancelReservation
- CreateRequest, CheckRequestStatus
- ServiceInformation (Solo servicios intangibles)
- ProductInformation (Solo productos físicos/catálogo)
- FaqQuery (Pagos, políticas, dudas generales)
- BusinessProfileQuery (Quiénes son)
- LocationQuery (Ubicación, sedes, cómo llegar)
- BusinessHoursQuery (Horarios)
- HumanHandoffRequest (Hablar con humano, reclamos)
- GeneralGreeting
- Unknown (Expresiones ambiguas, datos sueltos como 'mañana', 'principal', o preguntas como '¿cuánto cuesta?')

CONTEXTO DE LA CONVERSACIÓN ACTUAL:{contextInfo}

REGLA CRÍTICA: Si el mensaje es corto (ej. 'la principal', 'a las 5') y hay una 'Acción Pendiente', clasifícalo siempre como 'Unknown'. El sistema lo procesará usando el contexto.
NUNCA inventes o deduzcas GUIDs, extrae los parámetros textualmente.

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
            _logger.LogError(ex, "Fallo crítico al clasificar la intención del mensaje mediante IA.");
            return new IntentResultDto(IntentType.ProviderUnavailable, 0, new Dictionary<string, string>());
        }
    }

    private record RawIntentDto(string Intent, double Confidence, Dictionary<string, string> Parameters);
}