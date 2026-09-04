#nullable enable
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Cache;
using StackExchange.Redis;

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
            _logger.LogWarning(ex, "Degradación: No se pudo guardar el contexto en Redis para {Phone}.", customerPhone);
        }
    }

    public async Task<ConversationContextDto?> GetContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"workspace:{workspaceId}:conversation:{customerPhone}:context";
            var value = await _redisDb.StringGetAsync(key);

            if (!value.HasValue || string.IsNullOrWhiteSpace(value.ToString()))
                return null;

            // 🔥 SPRINT 1.1: El operador "!" asegura al compilador que si fallara, caería en el catch.
            var context = JsonSerializer.Deserialize<ConversationContextDto>(value.ToString());
            return context!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Degradación: Redis no disponible o JSON inválido. Devolviendo contexto vacío para {Phone}.", customerPhone);
            return null; // El despachador asumirá una conversación nueva
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
            _logger.LogWarning(ex, "Degradación: No se pudo marcar el mensaje en caché.");
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
            _logger.LogWarning(ex, "Degradación: Falla al verificar origen del mensaje.");
            return false;
        }
    }
}