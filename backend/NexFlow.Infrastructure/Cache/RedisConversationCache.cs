using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using NexFlow.Application.Abstractions.Cache;

namespace NexFlow.Infrastructure.Cache;

public class RedisConversationCache : IConversationCache
{
    private readonly IDatabase _redisDb;

    public RedisConversationCache(IConnectionMultiplexer redis)
    {
        _redisDb = redis.GetDatabase();
    }

    public async Task SetContextAsync(Guid workspaceId, string customerPhone, ConversationContextDto context, CancellationToken cancellationToken)
    {
        var key = $"workspace:{workspaceId}:conversation:{customerPhone}:context";
        context.LastUpdated = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(context);
        await _redisDb.StringSetAsync(key, json, TimeSpan.FromMinutes(30));
    }

    public async Task<ConversationContextDto?> GetContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        var key = $"workspace:{workspaceId}:conversation:{customerPhone}:context";
        var value = await _redisDb.StringGetAsync(key);

        if (!value.HasValue) return null;

        try
        {
            return JsonSerializer.Deserialize<ConversationContextDto>(value.ToString());
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> TryAcquireMessageLockAsync(string messageId, CancellationToken cancellationToken)
    {
        var key = $"webhook:processed:{messageId}";
        // 🔥 CORRECCIÓN HARDENING: Reducimos el TTL de 24 horas a 2 minutos. 
        // Esto bloquea los webhooks duplicados instantáneos, pero libera el candado 
        // rápidamente por si nuestra API falla y n8n/Evolution necesita reintentar.
        return await _redisDb.StringSetAsync(key, "locked", TimeSpan.FromMinutes(2), When.NotExists);
    }
}