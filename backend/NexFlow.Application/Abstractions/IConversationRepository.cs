using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Abstractions;

public interface IConversationRepository
{
    // Obtiene la conversación activa de un número, si existe
    Task<ConversationRecord?> GetActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken);

    // Crea el hilo de la conversación
    Task CreateConversationAsync(Guid workspaceId, ConversationRecord conversation, CancellationToken cancellationToken);

    // Actualiza el modo (AUTOMATIC a HUMAN, etc)
    Task UpdateConversationModeAsync(Guid workspaceId, string conversationId, ConversationMode mode, CancellationToken cancellationToken);

    // Guarda el mensaje individual en la subcolección
    Task AddMessageAsync(Guid workspaceId, string conversationId, MessageRecord message, CancellationToken cancellationToken);

    // Para el Inbox Empresarial (FrontEnd)
    Task<IEnumerable<ConversationRecord>> GetRecentConversationsAsync(Guid workspaceId, int limit, CancellationToken cancellationToken);
    Task<IEnumerable<MessageRecord>> GetMessagesAsync(Guid workspaceId, string conversationId, int limit, CancellationToken cancellationToken);
    Task<ConversationRecord?> GetConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken);
}

public interface IConsumerIdentityRepository
{
    // Busca si este número ya interactuó antes con este negocio
    Task<ConsumerIdentityRecord?> GetConsumerAsync(Guid workspaceId, string phone, CancellationToken cancellationToken);

    // Inserta o actualiza la última fecha de interacción y el nombre (si lo dio)
    Task UpsertConsumerAsync(Guid workspaceId, ConsumerIdentityRecord consumer, CancellationToken cancellationToken);
}