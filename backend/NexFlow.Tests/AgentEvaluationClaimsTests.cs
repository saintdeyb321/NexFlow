using System.Security.Cryptography;
using System.Text;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class AgentEvaluationClaimsTests
{
    private sealed class Fixture
    {
        public InMemoryFirestore Store { get; } = new();
        public DateTime Now { get; set; } = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
        public AgentTurnRequest Request { get; } = new(Guid.NewGuid(), "synthetic-conversation", "synthetic-private-client", "synthetic-message", "synthetic-private-message");
        public FirestoreAgentEvaluationClaims Claims()
        {
            var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => Now);
            return new(Store.Db, clock.Object);
        }
        public string Path => $"workspaces/{Request.WorkspaceId}/conversationStates/ai-budget/shadowEvaluations/"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Request.SourceMessageId)));
    }

    [Fact]
    public async Task Atomic_claim_has_one_winner_and_survives_service_restart_and_budget_window_changes()
    {
        var f = new Fixture();
        var claims = await Task.WhenAll(f.Claims().TryBeginAsync(f.Request, default), f.Claims().TryBeginAsync(f.Request, default));
        Assert.Single(claims, c => c == AgentEvaluationClaim.Started); Assert.Single(claims, c => c == AgentEvaluationClaim.Duplicate);
        f.Now = f.Now.AddDays(2);
        Assert.Equal(AgentEvaluationClaim.Duplicate, await f.Claims().TryBeginAsync(f.Request, default));
        var marker = f.Store.Read(f.Path)!;
        Assert.Equal(3, marker.Fields.Count); Assert.Equal(1, marker.Fields["version"].IntegerValue);
        var stored = marker.Fields.ToString();
        Assert.DoesNotContain(f.Request.ClientKey, stored); Assert.DoesNotContain(f.Request.Text, stored);
        Assert.DoesNotContain(f.Request.SourceMessageId, stored); Assert.DoesNotContain(f.Request.ConversationId, stored);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("client")]
    [InlineData("conversation")]
    public async Task A_source_message_cannot_be_rebound_to_a_different_request(string field)
    {
        var f = new Fixture(); Assert.Equal(AgentEvaluationClaim.Started, await f.Claims().TryBeginAsync(f.Request, default));
        var changed = field switch { "text" => f.Request with { Text = "changed-message" },
            "client" => f.Request with { ClientKey = "another-client" }, _ => f.Request with { ConversationId = "another-conversation" } };
        Assert.Equal(AgentEvaluationClaim.Conflict, await f.Claims().TryBeginAsync(changed, default));
        Assert.Equal(AgentEvaluationClaim.Duplicate, await f.Claims().TryBeginAsync(f.Request, default));
    }

    [Fact]
    public async Task Identical_provider_message_ids_are_isolated_by_workspace()
    {
        var f = new Fixture(); var other = f.Request with { WorkspaceId = Guid.NewGuid() };
        Assert.Equal(AgentEvaluationClaim.Started, await f.Claims().TryBeginAsync(f.Request, default));
        Assert.Equal(AgentEvaluationClaim.Started, await f.Claims().TryBeginAsync(other, default));
        Assert.Equal(AgentEvaluationClaim.Duplicate, await f.Claims().TryBeginAsync(f.Request, default));
        Assert.Equal(AgentEvaluationClaim.Duplicate, await f.Claims().TryBeginAsync(other, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_uncertain_claims_never_reopen_a_marker_that_was_committed(bool afterCommit)
    {
        var f = new Fixture();
        if (afterCommit) f.Store.AfterCommit = _ => new IOException("synthetic lost acknowledgment");
        else f.Store.BeforeCommit = _ => new IOException("synthetic store failure");
        await Assert.ThrowsAsync<IOException>(() => f.Claims().TryBeginAsync(f.Request, default));
        f.Store.AfterCommit = null; f.Store.BeforeCommit = null;
        Assert.Equal(afterCommit ? AgentEvaluationClaim.Duplicate : AgentEvaluationClaim.Started,
            await f.Claims().TryBeginAsync(f.Request, default));
    }

    [Fact]
    public async Task Caller_cancellation_never_creates_a_claim()
    {
        var f = new Fixture(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Claims().TryBeginAsync(f.Request, cts.Token));
        Assert.Null(f.Store.Read(f.Path)); Assert.Equal(0, f.Store.CommitRpcCalls);
    }

    [Theory]
    [InlineData("source", 200)]
    [InlineData("client", 200)]
    [InlineData("conversation", 200)]
    [InlineData("text", 2000)]
    public async Task Identity_bounds_accept_the_limit_and_reject_the_next_character_before_storage(string field, int limit)
    {
        var f = new Fixture();
        AgentTurnRequest WithLength(int length) => field switch {
            "source" => f.Request with { SourceMessageId = new string('x', length) },
            "client" => f.Request with { ClientKey = new string('x', length) },
            "conversation" => f.Request with { ConversationId = new string('x', length) },
            _ => f.Request with { Text = new string('x', length) } };
        Assert.Equal(AgentEvaluationClaim.Started, await f.Claims().TryBeginAsync(WithLength(limit), default));
        var writes = f.Store.CommitRpcCalls;
        await Assert.ThrowsAsync<ArgumentException>(() => f.Claims().TryBeginAsync(WithLength(limit + 1), default));
        Assert.Equal(writes, f.Store.CommitRpcCalls);
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("clock")]
    public async Task Missing_workspace_and_unqualified_clock_cannot_create_claims(string kind)
    {
        var f = new Fixture(); var request = f.Request;
        if (kind == "workspace") request = request with { WorkspaceId = Guid.Empty };
        else f.Now = DateTime.SpecifyKind(f.Now, DateTimeKind.Unspecified);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Claims().TryBeginAsync(request, default));
        Assert.Equal(0, f.Store.CommitRpcCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Incomplete_or_unknown_marker_schema_is_never_treated_as_a_new_claim(long version)
    {
        var f = new Fixture();
        f.Store.Seed(f.Path, new Dictionary<string, Google.Cloud.Firestore.V1.Value> { ["version"] = new() { IntegerValue = version } });
        Assert.Equal(AgentEvaluationClaim.Conflict, await f.Claims().TryBeginAsync(f.Request, default));
        Assert.Equal(version, f.Store.Read(f.Path)!.Fields["version"].IntegerValue);
        Assert.Single(f.Store.Read(f.Path)!.Fields);
    }
}
