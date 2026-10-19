using System.Data.Common;
using System.Text.RegularExpressions;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

// Sprint 0: reproduction only. Pending acceptance tests intentionally expose current defects;
// do not change production code or weaken these expectations in the diagnostic sprint.
public sealed class Rc3Sprint0IncidentTests
{
    [Fact]
    public async Task Incident_sequence_with_healthy_simulated_dependencies_replies_five_times_and_hola_uses_no_ai()
    {
        var f = new AssistantConversationTests.Fixture();
        await f.Turn("Quiero informacion", "incident-1"); Assert.Contains("1. Consultar productos", f.Sent["incident-1"].Content);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation });
        await f.Turn("Como puedo hacer una reserva?", "incident-2");
        var draft = AssistantConversationTests.Fixture.BusinessDraft(await f.Context()); var calls = f.AiCalls;
        await f.Turn("Hola?", "incident-3"); Assert.Equal(calls, f.AiCalls);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProductQuery, QueryKind = OfferingQueryKind.Broad });
        await f.Turn("Que productos tienen", "incident-4");
        Assert.Equal(draft, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        await f.Turn("Escojo la opción 1", "incident-5");
        // Strict selection now clarifies the absent/current picker deterministically.
        Assert.Equal(5, f.Sent.Count); Assert.Equal(2, f.AiCalls);
        Assert.All(f.Sent.Values, response => { Assert.Equal(MessageStatus.Sent, response.Status); Assert.False(string.IsNullOrWhiteSpace(response.Content)); });
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    [Trait("RC3", "Sprint1Pending")]
    public async Task NF03_distinct_limited_inbounds_must_not_share_the_same_outbound_identity()
    {
        var f = new AssistantConversationTests.Fixture { Now = new DateTime(2026, 10, 19, 4, 8, 0, DateTimeKind.Utc) };
        for (var i = 0; i < 8; i++)
        {
            f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProductQuery, SearchTerm = "Producto real" });
            await f.Turn("precio del Producto real", $"independent-{i}");
        }
        Assert.Equal(6, f.AiCalls); // The quota remains intact; the defect is response identity.
        Assert.Equal(8, f.Sent.Count);
        await f.Turn("precio del Producto real", "independent-7"); Assert.Equal(8, f.Sent.Count);
    }

    [Fact]
    [Trait("RC3", "Sprint1Pending")]
    public async Task NF02_transient_profile_failure_before_ai_must_have_a_defined_unavailable_interpretation()
    {
        // Exercise the actual persistence boundary, including its transient-error
        // classification, rather than leaking an infrastructure exception from a mock.
        var firestore = new InMemoryFirestore { ReadFailure = new RpcException(new Status(StatusCode.Unavailable, "Simulated profile outage")) };
        var profiles = new FirestoreBusinessProfileRepository(firestore.Db); var workspace = Guid.NewGuid();
        var router = new Mock<IAiRouter>(MockBehavior.Strict);
        var interpreter = new AiInterpreter(router.Object, Mock.Of<IClock>(), profiles, NullLogger<AiInterpreter>.Instance);
        var result = await interpreter.InterpretAsync(workspace, "Como puedo hacer una reserva?", "", null, new() { "RESERVATIONS", "SERVICES" }, default);
        Assert.Equal(ConversationIntent.ProviderUnavailable, result.Intent); router.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("RC3", "Sprint2Pending")]
    public async Task NF05_an_explicit_option_phrase_under_a_current_menu_must_select_without_ai()
    {
        var f = new AssistantConversationTests.Fixture(); await f.Turn("Quiero informacion"); var c = await f.Context();
        var decision = await f.Recovery.RecoverAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, "chat",
            new("instance", AssistantConversationTests.Fixture.Phone, "Cliente", "Escojo la opción 1", "selection", false, f.Workspace), c, f.Modules, default);
        Assert.NotNull(decision?.Interpretation); Assert.Equal(ConversationIntent.ProductQuery, decision.Interpretation.Intent);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task NF01_actual_selection_predicate_blocks_a_following_hola_until_its_failed_predecessor_is_completed()
    {
        // Exercise the repository predicate, not a copied scheduling algorithm. SQLite supplies
        // regexp_replace and omits ONLY the PostgreSQL row-lock suffix. This does NOT validate
        // PostgreSQL SKIP LOCKED, production concurrency, or the Oracle incident's actual rows.
        using var sqlite = new SqliteConnection("Data Source=:memory:"); await sqlite.OpenAsync();
        sqlite.CreateFunction<string, string, string, string, string>("regexp_replace", (text, pattern, replacement, _) => Regex.Replace(text, pattern, replacement));
        await using var db = new NexFlowDbContext(new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(sqlite).AddInterceptors(new SelectorDialect()).Options);
        await db.Database.EnsureCreatedAsync(); var a = Workspace.Create("Fictitious A"); var b = Workspace.Create("Fictitious B");
        db.Workspaces.AddRange(a, b); var now = DateTime.UtcNow;
        var first = Envelope(a, "first", now.AddMinutes(-3), InboundMessageStatus.Failed); first.NextRetryAt = now.AddMinutes(5);
        var following = Envelope(a, "hola", now.AddMinutes(-2), InboundMessageStatus.Pending);
        var otherTenant = Envelope(b, "other-tenant-hola", now.AddMinutes(-1), InboundMessageStatus.Pending);
        db.InboundMessages.AddRange(first, following, otherTenant); await db.SaveChangesAsync();
        var repo = new InboundMessageRepository(db, Mock.Of<IInstanceResolver>(MockBehavior.Strict));
        var claimed = (await repo.GetAndLockNextMessagesAsync(10, default)).ToArray(); Assert.Equal(otherTenant.Id, Assert.Single(claimed).Id);
        Assert.Equal(InboundMessageStatus.Pending, following.Status);
        first.NextRetryAt = now.AddSeconds(-1); await db.SaveChangesAsync();
        var retry = Assert.Single(await repo.GetAndLockNextMessagesAsync(10, default)); Assert.Equal(first.Id, retry.Id);
        await repo.CompleteMessageAsync(retry.Id, retry.ProcessingStartedAt!.Value, default);
        Assert.Equal(following.Id, Assert.Single(await repo.GetAndLockNextMessagesAsync(10, default)).Id);
    }

    private static InboundMessage Envelope(Workspace workspace, string source, DateTime received, InboundMessageStatus status) => new()
    { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, InstanceName = workspace.EvolutionInstanceName!, ExternalMessageId = source,
        Phone = "+51999999999", PayloadJson = "{}", ReceivedAt = received, Status = status };

    private sealed class SelectorDialect : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            command.CommandText = command.CommandText.Replace("FOR UPDATE OF m SKIP LOCKED", "", StringComparison.Ordinal);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
