using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Abstractions.Cache;

public interface IConversationCache
{
    // Añadimos el Guid workspaceId para el aislamiento multi-tenant
    Task SetLastIntentAsync(Guid workspaceId, string customerPhone, string intent, CancellationToken cancellationToken);
    Task<string?> GetLastIntentAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken);
}