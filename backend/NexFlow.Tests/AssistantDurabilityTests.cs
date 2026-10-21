using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class AssistantDurabilityTests
{
    [Fact]
    public async Task Atomic_ai_budget_enforces_both_limits_with_concurrency_duplicates_and_next_window()
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationStateRepository(db.Db);
        var workspace = Guid.NewGuid(); var now = DateTime.UtcNow;
        db.SynchronizeReads($"workspaces/{workspace}/conversationStates/ai-budget");
        var admitted = await Task.WhenAll(repo.TryConsumeAiBudgetAsync(workspace, "+51911111111", "a", now, 1, 2, default), repo.TryConsumeAiBudgetAsync(workspace, "+51911111111", "b", now, 1, 2, default));
        Assert.Single(admitted, x => x);
        var duplicate = admitted[0] ? "a" : "b";
        Assert.True(await repo.TryConsumeAiBudgetAsync(workspace, "+51911111111", duplicate, now, 1, 2, default));
        Assert.True(await repo.TryConsumeAiBudgetAsync(workspace, "+51922222222", "c", now, 1, 2, default));
        Assert.False(await repo.TryConsumeAiBudgetAsync(workspace, "+51933333333", "d", now, 1, 2, default));
        Assert.True(await repo.TryConsumeAiBudgetAsync(workspace, "+51911111111", "new", now.AddMinutes(1), 1, 2, default));
        Assert.False(await repo.TryConsumeAiBudgetAsync(workspace, "+51911111111", "stale", now, 1, 2, default));
        Assert.True(await repo.TryConsumeAiBudgetAsync(Guid.NewGuid(), "+51911111111", "new", now, 1, 2, default));
    }

    [Fact]
    public async Task Recovery_options_and_budgets_are_isolated_by_workspace_and_stale_state_cannot_replace_a_menu()
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationStateRepository(db.Db); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var first = new ConversationContextDto { CurrentGoal = "ORDER", RecoveryOptions = new() { new("private-a", "Oferta A", "Service") } };
        var second = new ConversationContextDto { CurrentGoal = "RESERVATION", RecoveryOptions = new() { new("private-b", "Oferta B", "Service") } };
        await repo.UpsertStateAsync(a, "+51911111111", first, default); await repo.UpsertStateAsync(b, "+51911111111", second, default);
        var stale = (await repo.GetStateAsync(a, "+51911111111", default))!;
        first.RecoveryOptions.Clear(); await repo.UpsertStateAsync(a, "+51911111111", first, default);
        await Assert.ThrowsAsync<ConcurrencyException>(() => repo.UpsertStateAsync(a, "+51911111111", stale, default));
        Assert.Equal("private-b", Assert.Single((await repo.GetStateAsync(b, "+51911111111", default))!.RecoveryOptions).Key);
        Assert.Empty((await repo.GetStateAsync(a, "+51911111111", default))!.RecoveryOptions);
    }

    [Theory]
    [InlineData(AiFailureKind.Configuration)] [InlineData(AiFailureKind.InvalidResponse)] [InlineData(AiFailureKind.Timeout)]
    [InlineData(AiFailureKind.Unavailable)] [InlineData(AiFailureKind.RateLimit)]
    public async Task Provider_failure_preserves_draft_without_incrementing_semantic_clarifications_or_forcing_handoff(AiFailureKind failure)
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("ORDER"); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        for (var i = 0; i < 3; i++)
        {
            f.Interpretations.Enqueue(new() { Intent = failure == AiFailureKind.RateLimit ? ConversationIntent.RateLimited : ConversationIntent.ProviderUnavailable, Failure = failure });
            await f.Turn("consulta que requiere interpretación");
        }
        Assert.Equal(0, f.Handoffs); Assert.Equal(0, (await f.Context()).ClarificationAttempts);
        Assert.Equal(3, f.AiCalls); Assert.Equal(3, f.Sent.Count);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.All(f.Sent.Values, r => Assert.DoesNotContain("alta demanda", r.Content, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Consumer_ai_limit_replies_to_each_distinct_inbound_without_spending_more_ai()
    {
        var f = new AssistantConversationTests.Fixture();
        // Successful, factual interpretation resets clarification but still consumes the AI budget.
        for (var i = 0; i < 10; i++)
        { f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProductQuery, SearchTerm = "Producto real" }); await f.Turn("precio del Producto real"); }
        Assert.Equal(6, f.AiCalls); Assert.Equal(10, f.Sent.Count); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Persistent_menu_recovers_after_recreating_the_context_service_without_redis()
    {
        var f = new AssistantConversationTests.Fixture(); await f.Turn("menú");
        var cold = new ContextRecoveryService(Mock.Of<IConversationCache>(), new FirestoreConversationStateRepository(f.Firestore.Db));
        var state = await cold.GetOrRecoverContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, default);
        Assert.Equal("ProductQuery", state.RecoveryOptions[0].Key); Assert.NotNull(state.StateVersion);
        var before = JsonSerializer.Serialize(state.RecoveryOptions); await f.Turn("¿dónde están?");
        Assert.Equal(before, JsonSerializer.Serialize((await f.Context()).RecoveryOptions));
    }
}
