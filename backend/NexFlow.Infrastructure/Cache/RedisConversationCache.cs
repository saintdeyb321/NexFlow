using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using NexFlow.Application.Abstractions.Cache;

namespace NexFlow.Infrastructure.Cache;

public class RedisConversationCache : IConversationCache
{
    private readonly IDistributedCache _cache;

    public RedisConversationCache(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task SetLastIntentAsync(Guid workspaceId, string customerPhone, string intent, CancellationToken cancellationToken)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        };

        // Blindaje: La clave ahora pertenece exclusivamente a un negocio específico
        var key = $"workspace:{workspaceId}:conversation:{customerPhone}";
        await _cache.SetStringAsync(key, intent, options, cancellationToken);
    }

    public async Task<string?> GetLastIntentAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        var key = $"workspace:{workspaceId}:conversation:{customerPhone}";
        return await _cache.GetStringAsync(key, cancellationToken);
    }
}