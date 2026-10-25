using Microsoft.Extensions.Logging;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;

namespace NexFlow.Application.Features.AI.Router;

public enum AiTaskType
{
    IntentExtraction, // Rápido, barato
    ComplexChat,       // Contexto amplio, conversacional
    ResponseComposition
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
        using var taskScope = AiTelemetry.TaskScope(taskType);
        // 🔥 SPRINT 10: Enrutamiento seguro por Enum, inmune a refactorizaciones de clases
        IAiProvider primaryProvider = taskType == AiTaskType.IntentExtraction
            ? GetProvider(AiProviderType.Groq)
            : GetProvider(AiProviderType.Gemini);

        try
        {
            _logger.LogDebug("Intentando ejecutar tarea {TaskType} con {Provider}", taskType, primaryProvider.ProviderType);
            return await ConversationDiagnostics.MeasureAsync(_logger, "AiProvider",
                () => primaryProvider.GenerateTextAsync(systemPrompt, userPrompt, useJsonMode, ct),
                provider: primaryProvider.ProviderType.ToString(), reason: "Primary");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsTransientError(ex))
        {
            var fallbackProvider = GetProvider(taskType == AiTaskType.IntentExtraction ? AiProviderType.Gemini : AiProviderType.Groq);
            // 🔥 SPRINT 10: Solo ejecutamos Fallback si el error es transitorio (caída temporal)
            _logger.LogWarning("Fallo transitorio {FailureType} en {Provider}; fallback a {FallbackProvider}.", ex.GetType().Name, primaryProvider.ProviderType, fallbackProvider.ProviderType);

            try
            {
                return await ConversationDiagnostics.MeasureAsync(_logger, "AiProvider",
                    () => fallbackProvider.GenerateTextAsync(systemPrompt, userPrompt, useJsonMode, ct),
                    provider: fallbackProvider.ProviderType.ToString(), reason: FallbackReason(ex));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception fallbackEx)
            {
                _logger.LogError("Fallback IA {Provider} falló: {FailureType}.", fallbackProvider.ProviderType, fallbackEx.GetType().Name);
                throw; // Preserve the provider HTTP status for the interpreter.
            }
        }
        catch (Exception ex)
        {
            // 🔥 SPRINT 10: Si el error es 400 (Bad Request), 401 (Auth) o formato JSON, abortamos sin gastar saldo en el fallback.
            _logger.LogError("Error IA no recuperable {FailureType} en {Provider}; sin fallback.", ex.GetType().Name, primaryProvider.ProviderType);
            throw;
        }
    }

    private IAiProvider GetProvider(AiProviderType type)
    {
        return _providers.FirstOrDefault(p => p.ProviderType == type)
               ?? throw new AiProviderConfigurationException(type.ToString());
    }

    private static bool IsTransientError(Exception ex) => ex switch
    {
        TimeoutException or OperationCanceledException => true,
        HttpRequestException http => http.StatusCode == null ||
            http.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.RequestTimeout ||
            (int)http.StatusCode.Value >= 500,
        _ => false
    };

    private static string FallbackReason(Exception ex) => ex switch
    {
        TimeoutException or OperationCanceledException => "Timeout",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => "RateLimit",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.RequestTimeout } => "Timeout",
        HttpRequestException { StatusCode: null } => "Network",
        _ => "Unavailable"
    };
}
