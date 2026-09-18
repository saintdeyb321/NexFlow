using NexFlow.Application.Engines.AI;

namespace NexFlow.Application.Features.AI.Router;

public enum AiTaskType
{
    IntentExtraction, // Rápido, barato (Groq)
    ComplexChat       // Contexto amplio, conversacional (Gemini)
}

public interface IAiRouter
{
    IAiProvider GetProvider(AiTaskType taskType);
}

public class AiRouter : IAiRouter
{
    private readonly IEnumerable<IAiProvider> _providers;

    public AiRouter(IEnumerable<IAiProvider> providers)
    {
        _providers = providers;
    }

    public IAiProvider GetProvider(AiTaskType taskType)
    {
        if (taskType == AiTaskType.IntentExtraction)
        {
            // Busca Groq, si falla (ej. no inyectado), hace fallback al primero que encuentre (Gemini)
            return _providers.FirstOrDefault(p => p.GetType().Name.Contains("Groq")) ?? _providers.First();
        }

        // Para ComplexChat, busca Gemini
        return _providers.FirstOrDefault(p => p.GetType().Name.Contains("Gemini")) ?? _providers.First();
    }
}