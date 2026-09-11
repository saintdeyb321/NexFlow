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

        var rawModel = configuration["Gemini:Model"] ?? "gemini-1.5-flash";
        _model = rawModel.Trim().Replace("models/", "");

        // 🔥 CORRECCIÓN: Le damos a la IA 25 segundos para respirar y pensar.
        var rawTimeout = configuration["Gemini:TimeoutSeconds"];
        var timeout = int.TryParse(rawTimeout, out var t) && t > 0 ? t : 25;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var safeSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? "Eres un asistente virtual corporativo." : systemPrompt;
        var safeUserMessage = string.IsNullOrWhiteSpace(userMessage) ? "Hola" : userMessage;

        object payload;
        var systemInstructionObj = new { parts = new[] { new { text = safeSystemPrompt } } };
        var contentsObj = new[] { new { role = "user", parts = new[] { new { text = safeUserMessage } } } };

        if (useJsonMode)
        {
            payload = new { system_instruction = systemInstructionObj, contents = contentsObj, generationConfig = new { response_mime_type = "application/json" } };
        }
        else
        {
            payload = new { system_instruction = systemInstructionObj, contents = contentsObj };
        }

        string jsonPayload = JsonSerializer.Serialize(payload);

        int maxRetries = 2;
        int delayMilliseconds = 1000;

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    int statusCode = (int)response.StatusCode;
                    if ((statusCode == 503 || statusCode == 429) && i < maxRetries)
                    {
                        await Task.Delay(delayMilliseconds, cancellationToken);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                using var jsonDocument = JsonDocument.Parse(responseString);

                if (jsonDocument.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    if (candidates[0].TryGetProperty("content", out var resContent) && resContent.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                    {
                        return parts[0].GetProperty("text").GetString() ?? string.Empty;
                    }
                }
                throw new InvalidOperationException("Respuesta vacía o formato inválido de Gemini.");
            }
            catch (TaskCanceledException ex)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                _logger.LogWarning("Timeout: Gemini tardó más de {TimeoutSeconds} segundos en el intento {Intento}.", _httpClient.Timeout.TotalSeconds, i + 1);
                if (i == maxRetries) throw;
            }
            catch (HttpRequestException ex)
            {
                if (i == maxRetries) throw;
                await Task.Delay(delayMilliseconds, cancellationToken);
            }
        }
        throw new Exception("Fallo general en la generación de texto de Gemini.");
    }
}