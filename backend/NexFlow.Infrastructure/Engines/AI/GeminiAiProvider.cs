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
        _apiKey = configuration["Gemini:ApiKey"]?.Trim() ?? throw new ArgumentNullException("Falta la API Key de Gemini en la configuración.");

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

        if (payloadContents.Any() && payloadContents.First()["role"].ToString() == "model")
        {
            payloadContents.Insert(0, new Dictionary<string, object> { { "role", "user" }, { "parts", new[] { new { text = "Continuemos." } } } });
        }

        var payload = new Dictionary<string, object>
        {
            { "system_instruction", new { parts = new[] { new { text = safeSystemPrompt } } } },
            { "contents", payloadContents }
        };

        if (tools != null && tools.Any())
        {
            payload["tools"] = new[] { new { functionDeclarations = tools.Select(t => new { name = t.Name, description = t.Description, parameters = t.ParametersSchema }).ToList() } };
        }

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
                var parts = jsonNode?["candidates"]?[0]?["content"]?["parts"];

                if (parts != null && parts.AsArray().Count > 0)
                {
                    // 1. Canal Oficial: Gemini usó la herramienta correctamente
                    var functionCall = parts[0]["functionCall"];
                    if (functionCall != null)
                    {
                        var name = functionCall["name"]?.ToString();
                        var args = functionCall["args"]?.AsObject();
                        if (name != null) return new AiResponse(null, new AiToolCall(name, args ?? new JsonObject()));
                    }

                    var text = parts[0]["text"]?.ToString();

                    // 2. 🔥 INTERCEPTOR: Gemini alucinó y escribió la herramienta como texto JSON RAW
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var cleanText = text.Trim();
                        // Limpiar formato Markdown si existe
                        if (cleanText.StartsWith("```json")) cleanText = cleanText.Replace("```json", "").Replace("```", "").Trim();

                        // Si parece una herramienta, la secuestramos y la ejecutamos
                        if (cleanText.StartsWith("{") && cleanText.Contains("\"name\"") && cleanText.Contains("\"arguments\""))
                        {
                            try
                            {
                                var parsedJson = JsonNode.Parse(cleanText);
                                var toolName = parsedJson?["name"]?.ToString();
                                var toolArgs = parsedJson?["arguments"]?.AsObject();

                                if (toolName != null)
                                {
                                    _logger.LogInformation("Interceptor activado: Se capturó JSON RAW y se convirtió en ToolCall para {ToolName}", toolName);
                                    return new AiResponse(null, new AiToolCall(toolName, toolArgs ?? new JsonObject()));
                                }
                            }
                            catch
                            {
                                // Si falla el parseo, lo ignoramos y lo mandamos como texto normal
                            }
                        }
                    }

                    return new AiResponse(text, null);
                }

                throw new InvalidOperationException($"Respuesta vacía o formato inválido de Gemini ({modelName}).");
            }
            catch (TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                if (i == maxRetries) throw new TimeoutException($"Gemini ({modelName}) excedió el tiempo de espera de {_httpClient.Timeout.TotalSeconds}s.");
            }
        }
        throw new Exception($"Fallo general en la generación de Gemini ({modelName}).");
    }
}