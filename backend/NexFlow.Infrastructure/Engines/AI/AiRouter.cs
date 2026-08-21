using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Infrastructure.Engines.AI;

public class AiRouter : IAiRouter
{
    private readonly IAiProvider _aiProvider;

    public AiRouter(IAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public async Task<string> GenerateResponseAsync(Guid workspaceId, IntentResultDto intent, string systemContext, CancellationToken cancellationToken)
    {
        // Patron Strategy simplificado para decidir la personalidad de la IA según la intención
        var basePrompt = intent.Intent switch
        {
            IntentType.Faq => "Eres un asistente de servicio al cliente. Responde la duda basándote ÚNICAMENTE en esta información: ",
            IntentType.CheckAvailability => "Eres un recepcionista. Muestra estos horarios disponibles de forma amable: ",
            IntentType.Unknown => "Eres un asistente. Dile al cliente amablemente que no le entendiste y ofrécele opciones (ej: reservar, preguntar).",
            _ => "Eres un asistente virtual de reservas. Sé breve y amable. Contexto: "
        };

        var finalSystemPrompt = $"{basePrompt} \n{systemContext}";

        // El userMessage se pasa vacío o con parámetros clave dependiendo de la necesidad, 
        // por ahora dejamos a la IA armar la respuesta con el contexto.
        return await _aiProvider.GenerateTextAsync(finalSystemPrompt, "Genera la respuesta al cliente.", useJsonMode: false, cancellationToken);
    }
}