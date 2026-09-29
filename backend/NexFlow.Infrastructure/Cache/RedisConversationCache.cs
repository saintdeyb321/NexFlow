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

            var context = JsonSerializer.Deserialize<ConversationContextDto>(value.ToString());
            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Degradación: Redis no disponible o JSON inválido para {Phone}.", customerPhone);
            return null;
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

    // 🔥 SPRINT 03: Usamos Redis para identificar instantáneamente los mensajes que nosotros mismos disparamos.
    public async Task MarkMessageAsAiGeneratedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        try
        {
            // Limpiamos la clave para evitar fallos por si llega con el prefijo "response:X"
            var cleanMessageId = messageId.Contains(':') ? messageId.Split(':')[0] : messageId;
            var key = $"workspace:{workspaceId}:aimessage:{cleanMessageId}";
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
            var cleanMessageId = messageId.Contains(':') ? messageId.Split(':')[0] : messageId;
            var key = $"workspace:{workspaceId}:aimessage:{cleanMessageId}";
            return await _redisDb.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Degradación: Falla al verificar origen del mensaje.");
            return false;
        }
    }
}