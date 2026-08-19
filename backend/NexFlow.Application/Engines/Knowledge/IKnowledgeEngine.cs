using NexFlow.Application.Features.Knowledge;

namespace NexFlow.Application.Engines.Knowledge;

public interface IKnowledgeEngine
{
    // Para el panel de administración (CRUD básico)
    Task<IEnumerable<FaqDto>> GetAllFaqsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveFaqAsync(Guid workspaceId, FaqDto faq, CancellationToken cancellationToken);
    Task DeleteFaqAsync(Guid workspaceId, string faqId, CancellationToken cancellationToken);

    // Para el flujo conversacional (El bot buscando respuestas)
    // Extrae FAQs que coincidan con la intención o pregunta del cliente
    Task<IEnumerable<FaqDto>> SearchRelevantFaqsAsync(Guid workspaceId, string query, CancellationToken cancellationToken);

    // Obtiene información estructurada sobre el negocio (Horarios, ubicaciones) en formato texto para inyectar en prompts
    Task<string> GetBusinessContextAsStringAsync(Guid workspaceId, CancellationToken cancellationToken);
}