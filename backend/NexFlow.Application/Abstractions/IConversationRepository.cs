using NexFlow.Application.Features.Automation.Conversations;

namespace NexFlow.Application.Abstractions;

public interface IConversationRepository
{
    // Trae los últimos X mensajes para darle contexto a la IA (probablemente lo guardaremos en Redis o Firestore)
    Task<ConversationContextDto?> GetContextAsync(Guid workspaceId, string customerIdentifier, int limit, CancellationToken cancellationToken);

    // Guarda un nuevo mensaje en el historial
    Task SaveMessageAsync(Guid workspaceId, string customerIdentifier, MessageDto message, CancellationToken cancellationToken);
}