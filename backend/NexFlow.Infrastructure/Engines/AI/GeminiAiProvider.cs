using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Router;

namespace NexFlow.Infrastructure.Engines.AI;

public class GeminiAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _primaryModel;
    private readonly string _fallbackModel;
    private readonly ILogger<GeminiAiProvider> _logger;
    public AiProviderType ProviderType => AiProviderType.Gemini;

    public GeminiAiProvider(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiAiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"]?.Trim() ?? "";

        var rawModel = configuration["Gemini:Model"] ?? "";
        _primaryModel = rawModel.Trim().Replace("models/", "");

        var rawFallback = configuration["Gemini:FallbackModel"] ?? "";
        _fallbackModel = rawFallback.Trim().Replace("models/", "");

        var rawTimeout = configuration["Gemini:TimeoutSeconds"];
        var timeout = int.TryParse(rawTimeout, out var t) && t is >= 1 and <= 60 ? t : 12;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
        _httpClient.MaxResponseContentBufferSize = 65_536;
        if (!string.IsNullOrWhiteSpace(_apiKey)) _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default)
    {
        var response = await GenerateChatResponseInternalAsync(systemPrompt, new List<AiMessage> { new("user", userMessage) }, null, useJsonMode, cancellationToken);
        return response.Text ?? string.Empty;
    }

    public Task<AiResponse> GenerateChatResponseAsync(string systemPrompt, List<AiMessage> history, List<AiTool>? tools = null, CancellationToken cancellationToken = default)
    {
        return GenerateChatResponseInternalAsync(systemPrompt, history, tools, false, cancellationToken);
    }

    private async Task<AiResponse> GenerateChatResponseInternalAsync(string systemPrompt, List<AiMessage> history, List<AiTool>? tools, bool useJsonMode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || !ValidModel(_primaryModel)) throw new AiProviderConfigurationException("Gemini");
        var safeSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? "Eres un asistente virtual corporativo." : systemPrompt;

        var payloadContents = new List<Dictionary<string, object>>();
        string? currentRole = null;
        string currentText = string.Empty;

        foreach (var msg in history)
        {
            var role = msg.Role.ToLowerInvariant() == "assistant" ? "model" : "user";

            if (currentRole == role)
            {
                currentText += $"\n{msg.Text}";
            }
            else
            {
                if (currentRole != null)
                {
                    payloadContents.Add(new Dictionary<string, object> { { "role", currentRole }, { "parts", new[] { new { text = currentText } } } });
                }
                currentRole = role;
                currentText = msg.Text;
            }
        }

        if (currentRole != null)
        {
            payloadContents.Add(new Dictionary<string, object> { { "role", currentRole }, { "parts", new[] { new { text = currentText } } } });
        }

        // Gemini requiere que el último mensaje de la historia no sea del modelo para poder generar una respuesta
        if (payloadContents.Any() && payloadContents.Last()["role"].ToString() == "model")
        {
            payloadContents.Add(new Dictionary<string, object> { { "role", "user" }, { "parts", new[] { new { text = "Continuemos." } } } });
        }

        var payload = new Dictionary<string, object>
        {
            { "system_instruction", new { parts = new[] { new { text = safeSystemPrompt } } } },
            { "contents", payloadContents }
        };

        // 🔥 SPRINT 8: Aplicación estricta de JSON Mode si es requerido
        if (useJsonMode)
        {
            payload["generationConfig"] = new
            {
                responseMimeType = "application/json",
                temperature = 0.0,
                maxOutputTokens = 1024
            };
        }

        string jsonPayload = JsonSerializer.Serialize(payload);

        try
        {
            return await ExecuteRequestAsync(_primaryModel, jsonPayload, cancellationToken);
        }
        catch (HttpRequestException ex) when (ValidModel(_fallbackModel) && _fallbackModel != _primaryModel
            && (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || (int?)ex.StatusCode == 429))
        {
            _logger.LogWarning("Modelo principal {Model} saturado. Activando fallback a {Fallback}", _primaryModel, _fallbackModel);
            return await ExecuteRequestAsync(_fallbackModel, jsonPayload, cancellationToken);
        }
    }

    private static bool ValidModel(string model) => !string.IsNullOrWhiteSpace(model) && model.Length <= 100
        && model.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private async Task<AiResponse> ExecuteRequestAsync(string modelName, string jsonPayload, CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelName)}:generateContent";
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        using var response = await AiTelemetry.SendAsync(ProviderType, modelName != _primaryModel,
            () => _httpClient.PostAsync(url, content, cancellationToken), _logger, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gemini API Error {StatusCode}.", response.StatusCode);
            throw new HttpRequestException($"Gemini HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }
        try
        {
            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(responseString);
            AiTelemetry.Usage(ProviderType, json.RootElement, _logger);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0 && candidates[0].ValueKind == JsonValueKind.Object && candidates[0].TryGetProperty("content", out var candidate) && candidate.ValueKind == JsonValueKind.Object
                && candidate.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array
                && parts.GetArrayLength() > 0 && parts[0].ValueKind == JsonValueKind.Object && parts[0].TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString()))
                return new AiResponse(text.GetString(), null);
            throw new AiInvalidResponseException("Gemini");
        }
        catch (JsonException) { throw new AiInvalidResponseException("Gemini"); }
    }
}
