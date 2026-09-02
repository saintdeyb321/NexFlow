using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Cache;
using StackExchange.Redis;
using System.Text.Json;

namespace NexFlow.Infrastructure.Cache;

public class RedisConversationCache : IConversationCache
{
    private readonly IDatabase _redisDb;
    private readonly ILogger<RedisConversationCache> _logger;

    public RedisConversationCache(IConnectionMultiplexer redis, ILogger<RedisConversationCache> logger)
    {
        _redisDb = redis.GetDatabase();
        _logger = logger;
    }

    public async Task SetContextAsync(Guid workspaceId, string customerPhone, ConversationContextDto context, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"workspace:{workspaceId}:conversation:{customerPhone}:context";
            context.LastUpdated = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(context);
            await _redisDb.StringSetAsync(key, json, TimeSpan.FromMinutes(30));
        }
        catch (Exception ex)
        {
            // 🔥 Auditoría (Sprint 1.1): Comportamiento degradado. Falla silenciosamente y permite que el bot responda sin memoria.
            _logger.LogWarning(ex, "Degradación: No se pudo guardar el contexto en Redis para {Phone}.", customerPhone);
        }
    }

    public async Task<ConversationContextDto?> GetContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"workspace:{workspaceId}:conversation:{customerPhone}:context";
            var value = await _redisDb.StringGetAsync(key);

            if (!value.HasValue) return null;

            return JsonSerializer.Deserialize<ConversationContextDto>(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Degradación: Redis no disponible. Devolviendo contexto vacío para {Phone}.", customerPhone);
            return null; // El despachador asumirá una conversación nueva en lugar de lanzar excepción 500.
        }
    }

    public async Task DeleteContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"workspace:{workspaceId}:conversation:{customerPhone}:context";
            await _redisDb.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Degradación: No se pudo eliminar el contexto en Redis para {Phone}.", customerPhone);
        }
    }

    public async Task MarkMessageAsAiGeneratedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"workspace:{workspaceId}:aimessage:{messageId}";
            await _redisDb.StringSetAsync(key, "1", TimeSpan.FromMinutes(10));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Degradación: No se pudo marcar el mensaje {MessageId} en caché.", messageId);
        }
    }

    public async Task<bool> IsMessageAiGeneratedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"workspace:{workspaceId}:aimessage:{messageId}";
            return await _redisDb.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Degradación: Falla al verificar origen del mensaje. Asumiendo falso para {MessageId}.", messageId);
            return false;
        }
    }
}