using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        _model = configuration["Gemini:Model"] ?? "gemini-1.5-flash"; // Fallback seguro

        // Timeout dinámico para evitar que la app se congele si Google se cae
        var timeout = int.TryParse(configuration["Gemini:TimeoutSeconds"], out var t) ? t : 15;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { parts = new[] { new { text = userMessage } } } }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDocument = JsonDocument.Parse(responseString);

            // NAVEGACIÓN SEGURA (Sin indexación directa que explote)
            if (jsonDocument.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                if (candidates[0].TryGetProperty("content", out var resContent) &&
                    resContent.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    var text = parts[0].GetProperty("text").GetString();
                    return text ?? string.Empty;
                }
            }

            _logger.LogWarning("Gemini devolvió un 200 OK, pero la estructura JSON no contenía texto válido.");
            return "Lo siento, tuve un problema procesando la información. ¿Podrías reformularlo?";
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Timeout: Gemini tardó demasiado en responder.");
            return "El servidor de inteligencia artificial está tardando demasiado. Por favor, intenta de nuevo en unos minutos.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo inesperado al comunicarse con Gemini.");
            return "En este momento tenemos intermitencias. Por favor, inténtalo más tarde.";
        }
    }
}