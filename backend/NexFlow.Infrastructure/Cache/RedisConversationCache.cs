using Microsoft.Extensions.Caching.Distributed;

namespace NexFlow.Infrastructure.Cache;

public interface IConversationCache
{
    Task SetLastIntentAsync(string customerPhone, string intent, CancellationToken cancellationToken);
    Task<string?> GetLastIntentAsync(string customerPhone, CancellationToken cancellationToken);
}

public class RedisConversationCache : IConversationCache
{
    private readonly IDistributedCache _cache;

    public RedisConversationCache(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task SetLastIntentAsync(string customerPhone, string intent, CancellationToken cancellationToken)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) // La memoria dura 30 mins
        };
        await _cache.SetStringAsync($"intent:{customerPhone}", intent, options, cancellationToken);
    }

    public async Task<string?> GetLastIntentAsync(string customerPhone, CancellationToken cancellationToken)
    {
        return await _cache.GetStringAsync($"intent:{customerPhone}", cancellationToken);
    }
}