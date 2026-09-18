using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Engines.AI;

namespace NexFlow.Infrastructure.Engines.AI;

public class GeminiAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _primaryModel;
    private readonly string _fallbackModel;
    private readonly ILogger<GeminiAiProvider> _logger;

    public GeminiAiProvider(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiAiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"]?.Trim() ?? throw new ArgumentNullException("Falta la API Key de Gemini.");

        var rawModel = configuration["Gemini:Model"] ?? "gemini-3.8-flash";
        _primaryModel = rawModel.Trim().Replace("models/", "");

        var rawFallback = configuration["Gemini:FallbackModel"] ?? "gemini-flash-latest";
        _fallbackModel = rawFallback.Trim().Replace("models/", "");

        var rawTimeout = configuration["Gemini:TimeoutSeconds"];
        var timeout = int.TryParse(rawTimeout, out var t) && t >= 30 ? t : 45;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default)
    {
        var response = await GenerateChatResponseAsync(systemPrompt, new List<AiMessage> { new("user", userMessage) }, null, cancellationToken);
        return response.Text ?? string.Empty;
    }

    public async Task<AiResponse> GenerateChatResponseAsync(string systemPrompt, List<AiMessage> history, List<AiTool>? tools = null, CancellationToken cancellationToken = default)
    {
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

        // Si en el futuro añadimos useJsonMode real nativo, iría en generationConfig aquí.

        string jsonPayload = JsonSerializer.Serialize(payload);

        try
        {
            return await ExecuteRequestAsync(_primaryModel, jsonPayload, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || (int?)ex.StatusCode == 429)
        {
            _logger.LogWarning("Modelo principal {Model} saturado. Activando fallback a {Fallback}", _primaryModel, _fallbackModel);
            return await ExecuteRequestAsync(_fallbackModel, jsonPayload, cancellationToken);
        }
    }

    private async Task<AiResponse> ExecuteRequestAsync(string modelName, string jsonPayload, CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={_apiKey}";
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        int maxRetries = 1;
        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                var response = await _httpClient.PostAsync(url, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);

                    if ((int)response.StatusCode == 429 && i < maxRetries)
                    {
                        await Task.Delay(1500, cancellationToken);
                        continue;
                    }

                    _logger.LogError("Gemini API Error {StatusCode} en modelo {Model}: {ErrorDetails}", response.StatusCode, modelName, error);
                    response.EnsureSuccessStatusCode();
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                var jsonNode = JsonNode.Parse(responseString);

                // 🔥 NAVEGACIÓN SEGURA DE JSON PARA EVITAR NULL REFERENCE EXCEPTIONS
                var candidates = jsonNode?["candidates"]?.AsArray();
                if (candidates != null && candidates.Count > 0)
                {
                    var parts = candidates[0]?["content"]?["parts"]?.AsArray();
                    if (parts != null && parts.Count > 0)
                    {
                        var text = parts[0]?["text"]?.ToString();
                        return new AiResponse(text, null);
                    }
                }

                throw new InvalidOperationException($"Respuesta vacía o bloqueada por filtros de seguridad en Gemini ({modelName}).");
            }
            catch (TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                if (i == maxRetries) throw new TimeoutException($"Gemini ({modelName}) excedió el tiempo de espera.");
            }
        }
        throw new Exception($"Fallo general en la generación de Gemini ({modelName}).");
    }
}