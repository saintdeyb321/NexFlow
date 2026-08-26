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

    // 🔥 CORRECCIÓN (Aislamiento Multitenant): Forzamos la recepción del WorkspaceId en el candado
    public async Task<bool> TryAcquireMessageLockAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var key = $"workspace:{workspaceId}:webhook:processed:{messageId}";

        return await _redisDb.StringSetAsync(key, "locked", TimeSpan.FromMinutes(2), When.NotExists);
    }
}