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

        var rawModel = configuration["Gemini:Model"] ?? "gemini-1.5-flash"; // Actualizado al modelo estándar
        _model = rawModel.Trim().Replace("models/", "");

        // 🔥 SPRINT 11 (P0): Timeout bajado a 8 segundos. Si la IA no responde rápido, abortamos.
        // Las interacciones conversacionales en WhatsApp deben ser inmediatas.
        var rawTimeout = configuration["Gemini:TimeoutSeconds"];
        var timeout = int.TryParse(rawTimeout, out var t) && t > 0 ? t : 8;
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

        string jsonPayload = JsonSerializer.Serialize(payload);

        int maxRetries = 2; // Bajado a 2 reintentos para no exceder los 15-20 segundos totales
        int delayMilliseconds = 500; // Backoff base corto

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                // 🔥 SPRINT 11: Se debe instanciar StringContent DENTRO del loop.
                // HttpContent se "consume" al enviarse; reusarlo en un retry genera una excepción inmediata.
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    int statusCode = (int)response.StatusCode;

                    if ((statusCode == 503 || statusCode == 429) && i < maxRetries)
                    {
                        _logger.LogWarning("Gemini API saturada (Status {StatusCode}). Reintentando {RetryCount}/{MaxRetries} en {Delay}ms...", statusCode, i + 1, maxRetries, delayMilliseconds);
                        await Task.Delay(delayMilliseconds, cancellationToken);
                        delayMilliseconds = 1000; // Backoff fijo corto, no exponencial
                        continue;
                    }

                    _logger.LogError("Gemini rejected request. Model={Model}, Status={StatusCode}", _model, statusCode);
                    response.EnsureSuccessStatusCode(); // Lanza excepción para ser capturada abajo
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

                // Si la API devolvió HTTP 200 pero sin candidatos, es un fallo semántico del modelo.
                throw new InvalidOperationException("Respuesta vacía o formato inválido de Gemini.");
            }
            catch (TaskCanceledException ex)
            {
                // Diferenciamos si el usuario desconectó la llamada (CancellationToken) o si fue un Timeout del HttpClient
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Petición cancelada por el cliente.");
                    throw; // SPRINT 12: Propagamos hacia el orquestador
                }

                _logger.LogWarning("Timeout: Gemini tardó más de {TimeoutSeconds} segundos en el intento {Intento}.", _httpClient.Timeout.TotalSeconds, i + 1);

                if (i == maxRetries)
                {
                    _logger.LogError(ex, "Timeout agotado tras {MaxRetries} reintentos.", maxRetries);
                    throw; // SPRINT 12: Propagamos para que el Orquestador devuelva el mensaje amable.
                }

                await Task.Delay(delayMilliseconds, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                if (i == maxRetries)
                {
                    _logger.LogError(ex, "Fallo crítico de red al comunicarse con Gemini después de {MaxRetries} reintentos.", maxRetries);
                    throw; // SPRINT 12
                }
                await Task.Delay(delayMilliseconds, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción inesperada en GeminiAiProvider en intento {Intento}.", i + 1);
                throw; // SPRINT 12
            }
        }

        throw new Exception("Fallo general en la generación de texto de Gemini.");
    }
}