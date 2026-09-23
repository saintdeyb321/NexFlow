using Microsoft.Extensions.Logging;
using NexFlow.Application.Engines.AI;

namespace NexFlow.Application.Features.AI.Router;

public enum AiTaskType
{
    IntentExtraction, // Rápido, barato
    ComplexChat       // Contexto amplio, conversacional
}

public interface IAiRouter
{
    Task<string> ExecuteTaskAsync(AiTaskType taskType, string systemPrompt, string userPrompt, bool useJsonMode, CancellationToken ct);
}

public class AiRouter : IAiRouter
{
    private readonly IEnumerable<IAiProvider> _providers;
    private readonly ILogger<AiRouter> _logger;

    public AiRouter(IEnumerable<IAiProvider> providers, ILogger<AiRouter> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<string> ExecuteTaskAsync(AiTaskType taskType, string systemPrompt, string userPrompt, bool useJsonMode, CancellationToken ct)
    {
        // 🔥 SPRINT 10: Enrutamiento seguro por Enum, inmune a refactorizaciones de clases
        IAiProvider primaryProvider = taskType == AiTaskType.IntentExtraction
            ? GetProvider(AiProviderType.Groq)
            : GetProvider(AiProviderType.Gemini);

        IAiProvider fallbackProvider = taskType == AiTaskType.IntentExtraction
            ? GetProvider(AiProviderType.Gemini)
            : GetProvider(AiProviderType.Groq);

        try
        {
            _logger.LogDebug("Intentando ejecutar tarea {TaskType} con {Provider}", taskType, primaryProvider.ProviderType);
            return await primaryProvider.GenerateTextAsync(systemPrompt, userPrompt, useJsonMode, ct);
        }
        catch (Exception ex) when (IsTransientError(ex))
        {
            // 🔥 SPRINT 10: Solo ejecutamos Fallback si el error es transitorio (caída temporal)
            _logger.LogWarning(ex, "Fallo transitorio (Timeout/RateLimit) en {Provider}. Tarea {TaskType}. Iniciando Fallback a {FallbackProvider}.", primaryProvider.ProviderType, taskType, fallbackProvider.ProviderType);

            try
            {
                return await fallbackProvider.GenerateTextAsync(systemPrompt, userPrompt, useJsonMode, ct);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Fallo catastrófico. Ambos proveedores IA ({Primary} y {Fallback}) fallaron para la tarea {TaskType}.", primaryProvider.ProviderType, fallbackProvider.ProviderType, taskType);
                throw new InvalidOperationException("Todos los servicios de inteligencia artificial están inactivos.", fallbackEx);
            }
        }
        catch (Exception ex)
        {
            // 🔥 SPRINT 10: Si el error es 400 (Bad Request), 401 (Auth) o formato JSON, abortamos sin gastar saldo en el fallback.
            _logger.LogError(ex, "Error de cliente o no recuperable en {Provider}. Tarea abortada sin fallback para evitar sobrecostos.", primaryProvider.ProviderType);
            throw;
        }
    }

    private IAiProvider GetProvider(AiProviderType type)
    {
        return _providers.FirstOrDefault(p => p.ProviderType == type)
               ?? _providers.First();
    }

    // Clasificador determinista de excepciones de API
    private static bool IsTransientError(Exception ex)
    {
        if (ex is TimeoutException || ex is TaskCanceledException) return true;

        if (ex is HttpRequestException httpEx)
        {
            var code = (int?)httpEx.StatusCode;
            // 429: Too Many Requests (Rate Limit). 5xx: Server Errors de la IA.
            if (code == 429 || (code >= 500 && code <= 599)) return true;

            // 400, 401, 403, 404 NO son transitorios (son culpa nuestra, no de Groq/Gemini).
            return false;
        }

        // Red de seguridad por si los SDKs (como Gemini SDK) envuelven la excepción HTTP
        var msg = ex.Message.ToLowerInvariant();
        if (msg.Contains("429") || msg.Contains("too many requests") || msg.Contains("500") || msg.Contains("503") || msg.Contains("timeout"))
            return true;

        return false;
    }
}