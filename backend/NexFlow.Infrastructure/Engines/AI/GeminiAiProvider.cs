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
        _apiKey = configuration["Gemini:ApiKey"] ?? throw new ArgumentNullException("Falta la API Key de Gemini");
        _model = configuration["Gemini:Model"] ?? "gemini-1.5-flash";

        var timeout = int.TryParse(configuration["Gemini:TimeoutSeconds"], out var t) ? t : 15;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        // Construcción dinámica del payload (Strategy Pattern simplificado)
        object payload;
        if (useJsonMode)
        {
            payload = new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[] { new { parts = new[] { new { text = userMessage } } } },
                generationConfig = new { response_mime_type = "application/json" }
            };
        }
        else
        {
            payload = new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[] { new { parts = new[] { new { text = userMessage } } } }
            };
        }

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

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