using NexFlow.Application.Abstractions.Cache;
using StackExchange.Redis;
using System.Text.Json;

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

    // 🔥 Auditoría (Fase 3): Este método originalmente inyectaba PostgreSQL aquí adentro. 
    // Ahora, simplemente verificamos un set de Idempotencia en Redis con un TTL corto para proteger el webhook.
    // Para persistencia fuerte (la tabla PostgreSQL ProcessedMessages), el Handler principal debe usar un Repositorio dedicado[cite: 2].
    public async Task<bool> TryAcquireMessageLockAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var key = $"workspace:{workspaceId}:idempotency:{messageId}";
        // Guarda un candado de 1 hora en Redis para evitar doble procesamiento del mismo webhook
        return await _redisDb.StringSetAsync(key, "locked", TimeSpan.FromHours(1), When.NotExists);
    }

    public async Task MarkMessageAsAiGeneratedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var key = $"workspace:{workspaceId}:aimessage:{messageId}";
        await _redisDb.StringSetAsync(key, "1", TimeSpan.FromMinutes(10));
    }

    public async Task<bool> IsMessageAiGeneratedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var key = $"workspace:{workspaceId}:aimessage:{messageId}";
        return await _redisDb.KeyExistsAsync(key);
    }
}