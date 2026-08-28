using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using StackExchange.Redis;
using System.Text.Json;

namespace NexFlow.Infrastructure.Cache;

public class RedisConversationCache : IConversationCache
{
    private readonly IDatabase _redisDb;
    private readonly IServiceScopeFactory _scopeFactory;

    public RedisConversationCache(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory)
    {
        _redisDb = redis.GetDatabase();
        _scopeFactory = scopeFactory;
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
    public async Task<bool> TryAcquireMessageLockAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();

        var record = new ProcessedMessage
        {
            WorkspaceId = workspaceId,
            MessageId = messageId,
            ProcessedAt = DateTime.UtcNow
        };
        db.ProcessedMessages.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}