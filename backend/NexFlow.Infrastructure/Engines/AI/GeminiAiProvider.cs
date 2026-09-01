using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Engines.AI;

namespace NexFlow.Infrastructure.Engines.AI;

public class GeminiAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiAiProvider> _logger;

    public GeminiAiProvider(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiAiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"]?.Trim() ?? throw new ArgumentNullException("Falta la API Key de Gemini");

        // 🔥 BLINDAJE: Limpiamos el nombre del modelo por si el appsettings trae espacios o el prefijo "models/"
        var rawModel = configuration["Gemini:Model"] ?? "gemini-1.5-flash";
        _model = rawModel.Trim().Replace("models/", "");

        var timeout = int.TryParse(configuration["Gemini:TimeoutSeconds"], out var t) ? t : 15;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var safeSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? "Eres un asistente virtual corporativo útil y amable." : systemPrompt;
        var safeUserMessage = string.IsNullOrWhiteSpace(userMessage) ? "Hola" : userMessage;

        // 🔥 ESTRUCTURA OFICIAL EXACTA DE GOOGLE GEMINI API (v1beta)
        object payload;
        var systemInstructionObj = new { parts = new[] { new { text = safeSystemPrompt } } };

        // La documentación oficial EXIGE incluir la propiedad "role" para identificar al emisor
        var contentsObj = new[]
        {
            new
            {
                role = "user",
                parts = new[] { new { text = safeUserMessage } }
            }
        };

        if (useJsonMode)
        {
            payload = new
            {
                system_instruction = systemInstructionObj,
                contents = contentsObj,
                generationConfig = new { response_mime_type = "application/json" }
            };
        }
        else
        {
            payload = new
            {
                system_instruction = systemInstructionObj,
                contents = contentsObj
            };
        }

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            // RADAR DE DEPURACIÓN
            if (!response.IsSuccessStatusCode)
            {
                var errorDetails = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"\n🚨 GOOGLE GEMINI RECHAZÓ LA PETICIÓN ({(int)response.StatusCode}) 🚨");
                Console.WriteLine($"URL Intentada: {url}");
                Console.WriteLine($"Detalle Oficial: {errorDetails}");
                Console.WriteLine("--------------------------------------------------\n");
                response.EnsureSuccessStatusCode();
            }

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDocument = JsonDocument.Parse(responseString);

            if (jsonDocument.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                if (candidates[0].TryGetProperty("content", out var resContent) &&
                    resContent.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    return parts[0].GetProperty("text").GetString() ?? string.Empty;
                }
            }
            return useJsonMode ? "{}" : string.Empty;
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Timeout: Gemini tardó demasiado.");
            return useJsonMode ? "{}" : "Lo siento, mi cerebro digital está tardando en procesar. ¿Puedes repetirlo?";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo inesperado al comunicarse con Gemini.");
            return useJsonMode ? "{}" : "Tuve un error interno de conexión. Vuelve a intentarlo en unos segundos.";
        }
    }
}