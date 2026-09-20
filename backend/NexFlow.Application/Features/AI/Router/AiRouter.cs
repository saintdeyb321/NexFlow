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
    // 🔥 SPRINT 11: Ahora el router devuelve el resultado garantizado (ejecutando Primary y haciendo Fallback si es necesario)
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
        IAiProvider primaryProvider;
        IAiProvider fallbackProvider;

        // 1. Definir Políticas de Enrutamiento Explícitas
        if (taskType == AiTaskType.IntentExtraction)
        {
            primaryProvider = GetProvider("GroqAiProvider");
            fallbackProvider = GetProvider("GeminiAiProvider");
        }
        else // ComplexChat
        {
            primaryProvider = GetProvider("GeminiAiProvider");
            fallbackProvider = GetProvider("GroqAiProvider"); // (O podrías usar Claude si lo integras)
        }

        // 2. Ejecutar con Fallback Robusto
        try
        {
            _logger.LogDebug("Intentando ejecutar tarea {TaskType} con {Provider}", taskType, primaryProvider.GetType().Name);
            return await primaryProvider.GenerateTextAsync(systemPrompt, userPrompt, useJsonMode, ct);
        }
        catch (Exception ex) when (ex is not TaskCanceledException) // No reintentamos si el usuario/timeout canceló la petición
        {
            _logger.LogWarning(ex, "Fallo en el proveedor primario {Provider} para tarea {TaskType}. Iniciando Fallback a {FallbackProvider}.", primaryProvider.GetType().Name, taskType, fallbackProvider.GetType().Name);

            try
            {
                return await fallbackProvider.GenerateTextAsync(systemPrompt, userPrompt, useJsonMode, ct);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Fallo catastrófico. Ambos proveedores de IA ({Primary} y {Fallback}) fallaron para la tarea {TaskType}.", primaryProvider.GetType().Name, fallbackProvider.GetType().Name, taskType);
                throw new InvalidOperationException("Todos los servicios de inteligencia artificial están inactivos.");
            }
        }
    }

    private IAiProvider GetProvider(string className)
    {
        return _providers.FirstOrDefault(p => p.GetType().Name == className)
               ?? _providers.First(); // Safe fallback por si no se inyectó correctamente
    }
}