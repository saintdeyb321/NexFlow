using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.AI;

public interface IAiRouter
{
    // El Router toma el contexto (ej: los horarios disponibles) y la intención, 
    // y decide qué modelo usar y cómo estructurar el prompt para generar la respuesta final.
    Task<string> GenerateResponseAsync(
        Guid workspaceId,
        IntentResultDto intent,
        string systemContext, // Aquí se inyectan las FAQs o los TimeSlots crudos
        CancellationToken cancellationToken);
}