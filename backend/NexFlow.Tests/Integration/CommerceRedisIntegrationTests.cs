using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Infrastructure.Cache;
using NexFlow.Infrastructure.Persistence.Firestore;
using StackExchange.Redis;
using Xunit;

namespace NexFlow.Tests.Integration;

public sealed class IsolatedRedisFactAttribute : FactAttribute
{
    public IsolatedRedisFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("NEXFLOW_CONCIERGE_REDIS_INTEGRATION")!="1" || Environment.GetEnvironmentVariable("NEXFLOW_RC3_INTEGRATION")!="1")
            Skip="NOT EXECUTED: explicit Redis + isolated PostgreSQL/Emulator opt-in required.";
    }
}

[Trait("Category","RedisIntegration")]
[Collection("RC3 isolated stores")]
public sealed class CommerceRedisIntegrationTests
{
    internal static ConfigurationOptions Options(string endpoint)
    {
        // A separate disposable instance/database; never the application Redis endpoint.
        if(endpoint!="127.0.0.1:16379") throw new InvalidOperationException("Disposable loopback Redis port 16379 required.");
        var options=ConfigurationOptions.Parse(endpoint);options.DefaultDatabase=15;
        options.ConnectTimeout=2000;options.SyncTimeout=2000;options.AsyncTimeout=2000;options.AbortOnConnectFail=true;
        return options;
    }

    [IsolatedRedisFact]
    public async Task Failed_invalidation_stale_copy_and_restart_keep_firestore_authoritative()
    {
        await using var stores=await IsolatedStores.Create();var ws=await stores.Workspace(false);
        var options=Options(Environment.GetEnvironmentVariable("NEXFLOW_CONCIERGE_REDIS_ENDPOINT")??"");
        using var inspect=await ConnectionMultiplexer.ConnectAsync(options);
        var key=$"workspace:{ws}:conversation:synthetic-client:context";
        try
        {
            using var first=await ConnectionMultiplexer.ConnectAsync(options);
            var cache=new RedisConversationCache(first,NullLogger<RedisConversationCache>.Instance);
            var states=new FirestoreConversationStateRepository(stores.Firestore);var context=new ContextRecoveryService(cache,states);
            await context.SaveContextAsync(ws,"synthetic-client",new(){CurrentGoal="ORDER",OrderDraftItems=[new(){ProductId="bread",ProductName="Synthetic",Quantity=2}]},stores.Ct);
            Assert.True(await inspect.GetDatabase().KeyExistsAsync(key));
            first.Dispose(); // Real client failure, no Redis restart or server mutation.
            await context.DeleteContextAsync(ws,"synthetic-client",stores.Ct);
            Assert.True(await inspect.GetDatabase().KeyExistsAsync(key));
            var restarted=new ContextRecoveryService(new RedisConversationCache(inspect,NullLogger<RedisConversationCache>.Instance),states);
            Assert.Empty((await restarted.GetOrRecoverContextAsync(ws,"synthetic-client",stores.Ct)).OrderDraftItems);
            Assert.False(await inspect.GetDatabase().KeyExistsAsync(key));
            await restarted.SaveContextAsync(ws,"synthetic-client",new(){CurrentGoal="ORDER",OrderDraftItems=[new(){ProductId="bread",ProductName="Synthetic",Quantity=4}]},stores.Ct);
            await inspect.GetDatabase().KeyExpireAsync(key,TimeSpan.FromMilliseconds(30));
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stores.Ct);timeout.CancelAfter(TimeSpan.FromSeconds(3));
            while(await inspect.GetDatabase().KeyExistsAsync(key)) await Task.Delay(20,timeout.Token);
            Assert.Equal(4,Assert.Single((await restarted.GetOrRecoverContextAsync(ws,"synthetic-client",stores.Ct)).OrderDraftItems).Quantity);
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new RedisConversationCache(inspect,NullLogger<RedisConversationCache>.Instance).DeleteContextAsync(ws,"synthetic-client",cancelled.Token));
        }
        finally { await inspect.GetDatabase().KeyDeleteAsync(key); } // Only this generated workspace key.
    }
}
