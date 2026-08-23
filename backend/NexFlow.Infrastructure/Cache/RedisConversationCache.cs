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
        await _redisDb.StringSetAsync(key, json, TimeSpan.FromMinutes(30)); // TTL: 30 minutos
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
            return null; // Si por alguna razón el JSON anterior era inválido/string viejo, reiniciamos.
        }
    }

    public async Task<bool> TryAcquireMessageLockAsync(string messageId, CancellationToken cancellationToken)
    {
        var key = $"webhook:processed:{messageId}";
        return await _redisDb.StringSetAsync(key, "locked", TimeSpan.FromHours(24), When.NotExists);
    }
}