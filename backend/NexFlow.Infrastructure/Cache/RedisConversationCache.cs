using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using NexFlow.Application.Abstractions.Cache;

namespace NexFlow.Infrastructure.Cache;

public class RedisConversationCache : IConversationCache
{
    private readonly IDatabase _redisDb;

    // Inyectamos el Multiplexer nativo para acceder a comandos atómicos
    public RedisConversationCache(IConnectionMultiplexer redis)
    {
        _redisDb = redis.GetDatabase();
    }

    public async Task SetLastIntentAsync(Guid workspaceId, string customerPhone, string intent, CancellationToken cancellationToken)
    {
        var key = $"workspace:{workspaceId}:conversation:{customerPhone}";
        await _redisDb.StringSetAsync(key, intent, TimeSpan.FromMinutes(30));
    }

    public async Task<string?> GetLastIntentAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        var key = $"workspace:{workspaceId}:conversation:{customerPhone}";
        var value = await _redisDb.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<bool> TryAcquireMessageLockAsync(string messageId, CancellationToken cancellationToken)
    {
        var key = $"webhook:processed:{messageId}";

        // When.NotExists equivale a SETNX en Redis. 
        // Es 100% ATÓMICO. Si 100 peticiones llegan al mismo tiempo, solo 1 recibirá 'true'.
        return await _redisDb.StringSetAsync(key, "locked", TimeSpan.FromHours(24), When.NotExists);
    }
}