using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Cache;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using StackExchange.Redis;
using Xunit;

namespace NexFlow.Tests;

public sealed class AssistantRedisRecoveryTests
{
    [Fact]
    public async Task Durable_deletion_succeeds_despite_redis_failure_and_stale_cache_never_resurrects_the_context()
    {
        var f = new Fixture(); await f.Seed(); f.FailDelete = true;
        await f.Contexts.DeleteContextAsync(f.Workspace, Fixture.Phone, default);
        Assert.Null(await f.States.GetStateAsync(f.Workspace, Fixture.Phone, default)); Assert.NotNull(f.Cached);
        for (var i = 0; i < 2; i++) AssertFresh(await f.Contexts.GetOrRecoverContextAsync(f.Workspace, Fixture.Phone, default));
        // Redis recovers: its stale entry is removed, without restoring durable state.
        f.FailDelete = false; AssertFresh(await f.Contexts.GetOrRecoverContextAsync(f.Workspace, Fixture.Phone, default));
        Assert.Null(f.Cached); AssertFresh(await f.Contexts.GetOrRecoverContextAsync(f.Workspace, Fixture.Phone, default));
    }

    [Fact]
    public async Task A_new_durable_decision_wins_over_the_copy_left_by_a_failed_redis_deletion()
    {
        var f = new Fixture(); await f.Seed(); f.FailDelete = true;
        await f.Contexts.DeleteContextAsync(f.Workspace, Fixture.Phone, default);
        var next = new ConversationContextDto { CurrentGoal = "RESERVATION", SelectedServiceId = "new-service", PendingRecoveryAction = "CANCEL", Mode = "Human" };
        await f.States.UpsertStateAsync(f.Workspace, Fixture.Phone, next, default);
        var recovered = await f.Contexts.GetOrRecoverContextAsync(f.Workspace, Fixture.Phone, default);
        Assert.Equal(next.StateVersion, recovered.StateVersion); Assert.Equal("new-service", recovered.SelectedServiceId);
        Assert.Equal("CANCEL", recovered.PendingRecoveryAction); Assert.Equal("Human", recovered.Mode); Assert.Empty(recovered.OrderDraftItems);
        Assert.Equal("old-service", f.Cached!.SelectedServiceId); // Even a failed/no-op cache refill cannot become authoritative.
    }

    [Fact]
    public async Task Expiration_of_the_stale_redis_entry_allows_cold_recovery_after_a_failed_invalidation()
    {
        var f = new Fixture(); await f.Seed(); f.FailDelete = true;
        await f.Contexts.DeleteContextAsync(f.Workspace, Fixture.Phone, default);
        f.Cached = null; // Simulated Redis TTL expiry; no clock sleeps or external Redis.
        var deletes = f.DeleteCalls;
        AssertFresh(await f.Contexts.GetOrRecoverContextAsync(f.Workspace, Fixture.Phone, default));
        Assert.Equal(deletes, f.DeleteCalls); Assert.Null(await f.States.GetStateAsync(f.Workspace, Fixture.Phone, default));
    }

    [Fact]
    public async Task A_failed_durable_deletion_is_propagated_without_invalidating_cache_or_losing_the_draft()
    {
        var f = new Fixture(); await f.Seed(); var before = JsonSerializer.Serialize(f.Cached);
        f.Store.BeforeCommit = _ => new IOException("Durable deletion unavailable");
        await Assert.ThrowsAsync<IOException>(() => f.Contexts.DeleteContextAsync(f.Workspace, Fixture.Phone, default));
        Assert.Equal(0, f.DeleteCalls); Assert.Equal(before, JsonSerializer.Serialize(f.Cached));
        f.Store.BeforeCommit = null;
        Assert.Equal(before, JsonSerializer.Serialize(await f.Contexts.GetOrRecoverContextAsync(f.Workspace, Fixture.Phone, default)));
    }

    [Fact]
    public async Task A_cancelled_caller_does_not_start_redis_deletion()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Cache.DeleteContextAsync(f.Workspace, Fixture.Phone, stop.Token));
        Assert.Equal(0, f.DeleteCalls);
    }

    [Fact]
    public async Task Cancellation_interrupts_waiting_for_redis_and_is_not_swallowed_as_a_cache_failure()
    {
        var f = new Fixture(); f.PendingDelete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        var deletion = f.Cache.DeleteContextAsync(f.Workspace, Fixture.Phone, stop.Token); Assert.Equal(1, f.DeleteCalls);
        stop.Cancel();
        try
        {
            var finished = await Task.WhenAny(deletion, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(deletion, finished);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => deletion);
        }
        finally { f.PendingDelete.TrySetResult(true); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Session_expiration_tolerates_failed_cache_invalidation_but_never_deletes_an_active_draft(bool activeDraft)
    {
        var f = new Fixture(); await f.Seed(activeDraft); f.FailDelete = true;
        var previous = (await f.States.GetStateAsync(f.Workspace, Fixture.Phone, default))!;
        var old = new ConversationRecord { Id = "old", ConsumerPhone = Fixture.Phone, Mode = ConversationMode.Automatic, LastMessageAt = DateTime.UtcNow.AddHours(-25) };
        var fresh = old with { Id = "fresh", LastMessageAt = DateTime.UtcNow };
        ConversationRecord? current = old;
        var conversations = new Mock<IConversationRepository>();
        conversations.Setup(c => c.GetActiveConversationAsync(f.Workspace, Fixture.Phone, default)).ReturnsAsync(() => current);
        conversations.Setup(c => c.CloseConversationAsync(f.Workspace, "old", default)).Callback(() => current = null).Returns(Task.CompletedTask);
        conversations.Setup(c => c.GetOrCreateActiveConversationAsync(f.Workspace, Fixture.Phone, default)).ReturnsAsync(() => current = fresh);
        var service = new ConversationStateService(conversations.Object, Mock.Of<IConsumerIdentityRepository>(), f.Contexts,
            Mock.Of<IHumanHandoffService>(MockBehavior.Strict), Mock.Of<INotificationService>(MockBehavior.Strict), NullLogger<ConversationStateService>.Instance);
        var result = await service.ProcessStateAsync(f.Workspace, Fixture.Phone, new("instance", Fixture.Phone, "Cliente", "hola", "incoming", false, f.Workspace), default);
        Assert.True(result.ShouldAiRespond); Assert.Null(result.FastReply);
        if (activeDraft)
        {
            Assert.Equal("old", result.Record.Id); Assert.Equal(0, f.DeleteCalls);
            Assert.Equal(JsonSerializer.Serialize(previous), JsonSerializer.Serialize(await f.States.GetStateAsync(f.Workspace, Fixture.Phone, default)));
        }
        else { Assert.Equal("fresh", result.Record.Id); AssertFresh(await f.Contexts.GetOrRecoverContextAsync(f.Workspace, Fixture.Phone, default)); }
    }

    private static void AssertFresh(ConversationContextDto c)
    {
        Assert.Null(c.StateVersion); Assert.Null(c.CurrentGoal); Assert.Null(c.SelectedServiceId); Assert.Null(c.PendingRecoveryAction);
        Assert.Equal("Automatic", c.Mode); Assert.Empty(c.RecoveryOptions); Assert.Empty(c.OrderDraftItems);
    }

    private sealed class Fixture
    {
        public const string Phone = "+51999999999";
        public Guid Workspace { get; } = Guid.NewGuid();
        public InMemoryFirestore Store { get; } = new();
        public FirestoreConversationStateRepository States { get; }
        public RedisConversationCache Cache { get; }
        public ContextRecoveryService Contexts { get; }
        public ConversationContextDto? Cached { get; set; }
        public bool FailDelete { get; set; }
        public int DeleteCalls { get; private set; }
        public TaskCompletionSource<bool>? PendingDelete { get; set; }
        public Fixture()
        {
            States = new(Store.Db); var database = new Mock<IDatabase>(); var connection = new Mock<IConnectionMultiplexer>();
            connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
            var key = $"workspace:{Workspace}:conversation:{Phone}:context";
            database.Setup(d => d.StringGetAsync((RedisKey)key, CommandFlags.None)).ReturnsAsync(() => Cached == null ? RedisValue.Null : (RedisValue)JsonSerializer.Serialize(Cached));
            database.Setup(d => d.KeyDeleteAsync((RedisKey)key, CommandFlags.None)).Returns(() =>
            {
                DeleteCalls++;
                if (PendingDelete != null) return PendingDelete.Task;
                if (FailDelete) return Task.FromException<bool>(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis outage"));
                Cached = null; return Task.FromResult(true);
            });
            Cache = new(connection.Object, NullLogger<RedisConversationCache>.Instance); Contexts = new(Cache, States);
        }
        public async Task Seed(bool activeDraft = true)
        {
            var c = new ConversationContextDto { CurrentGoal = activeDraft ? "ORDER" : null, SelectedServiceId = "old-service",
                RecoveryOptions = new() { new("old-offering", "Oferta anterior", "Service") },
                OrderDraftItems = activeDraft ? new() { new() { ProductId = "real-product", Quantity = 2 } } : new() };
            await States.UpsertStateAsync(Workspace, Phone, c, default); Cached = c;
        }
    }
}
