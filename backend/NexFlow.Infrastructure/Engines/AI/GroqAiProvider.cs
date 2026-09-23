using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Engines.AI;

namespace NexFlow.Infrastructure.Engines.AI;

public class GroqAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GroqAiProvider> _logger;
    public AiProviderType ProviderType => AiProviderType.Groq;

    public GroqAiProvider(HttpClient httpClient, IConfiguration configuration, ILogger<GroqAiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Groq:ApiKey"]?.Trim() ?? "";
        _model = configuration["Groq:Model"] ?? "llama3-8b-8192";

        var rawTimeout = configuration["Groq:TimeoutSeconds"];
        _httpClient.Timeout = TimeSpan.FromSeconds(int.TryParse(rawTimeout, out var t) ? t : 5);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userMessage }
        };

        var payload = new Dictionary<string, object>
        {
            { "model", _model },
            { "messages", messages },
            { "temperature", 0.0 } // 0.0 para máximo determinismo en extracción JSON
        };

        if (useJsonMode)
        {
            payload.Add("response_format", new { type = "json_object" });
        }

        string jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("https://api.groq.com/openai/v1/chat/completions", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Groq API Error {StatusCode}: {ErrorDetails}. Fallback será manejado por el Router.", response.StatusCode, error);
                throw new HttpRequestException($"Groq Fallo: {response.StatusCode}");
            }

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonNode = JsonNode.Parse(responseString);
            var text = jsonNode?["choices"]?[0]?["message"]?["content"]?.ToString();

            return text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo general en Groq. Revisa Rate Limits o API Key.");
            throw;
        }
    }

    public Task<AiResponse> GenerateChatResponseAsync(string systemPrompt, List<AiMessage> history, List<AiTool>? tools = null, CancellationToken cancellationToken = default)
    {
        // Groq se usará primariamente para GenerateTextAsync (Extracción JSON).
        // Si se llama aquí, devolvemos un fallo controlado para que actúe Gemini.
        throw new NotImplementedException("Groq provider is optimized for Intent Extraction only in this sprint.");
    }
}