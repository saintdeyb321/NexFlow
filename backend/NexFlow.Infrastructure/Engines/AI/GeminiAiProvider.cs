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

        var rawModel = configuration["Gemini:Model"] ?? "gemini-3.6-flash";
        _model = rawModel.Trim().Replace("models/", "");

        var timeout = int.TryParse(configuration["Gemini:TimeoutSeconds"], out var t) ? t : 15;
        _httpClient.Timeout = TimeSpan.FromSeconds(timeout);
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var safeSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? "Eres un asistente virtual corporativo útil y amable." : systemPrompt;
        var safeUserMessage = string.IsNullOrWhiteSpace(userMessage) ? "Hola" : userMessage;

        object payload;
        var systemInstructionObj = new { parts = new[] { new { text = safeSystemPrompt } } };

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

        // 🔥 Auditoría (Sprint 1.2): Política de reintentos con backoff exponencial (1s, 2s, 4s).
        int maxRetries = 3;
        int delayMilliseconds = 1000;

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                var response = await _httpClient.PostAsync(url, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    int statusCode = (int)response.StatusCode;

                    // Si el error es 503 (Unavailable) o 429 (Too Many Requests), aplicamos backoff.
                    if ((statusCode == 503 || statusCode == 429) && i < maxRetries)
                    {
                        _logger.LogWarning("Gemini API saturada (Status {StatusCode}). Reintentando {RetryCount}/{MaxRetries} en {Delay}ms...", statusCode, i + 1, maxRetries, delayMilliseconds);
                        await Task.Delay(delayMilliseconds, cancellationToken);
                        delayMilliseconds *= 2;
                        continue;
                    }

                    // 🔥 Auditoría (Sprint 1.2): Log seguro sin filtrar la API Key.
                    _logger.LogError("Gemini rejected request. Model={Model}, Status={StatusCode}", _model, statusCode);
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
                _logger.LogError("Timeout: Gemini tardó demasiado en responder.");
                return useJsonMode ? "{}" : "Lo siento, mi cerebro digital está tardando en procesar. ¿Puedes repetirlo?";
            }
            catch (HttpRequestException ex) when (i == maxRetries)
            {
                _logger.LogError(ex, "Fallo crítico persistente al comunicarse con Gemini después de {MaxRetries} reintentos.", maxRetries);
                return useJsonMode ? "{}" : "Tuve un error interno de conexión. Vuelve a intentarlo en unos segundos.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción inesperada en GeminiAiProvider.");
                return useJsonMode ? "{}" : "Ha ocurrido un fallo inesperado procesando tu mensaje.";
            }
        }

        return useJsonMode ? "{}" : string.Empty;
    }
}