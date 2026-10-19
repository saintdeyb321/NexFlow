using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class AssistantRc1ConcurrencyTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Handoff_or_context_change_during_ai_invalidates_the_turn_before_a_business_effect(bool human)
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("ORDER"); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.DuringInterpretation = async () => { var context = await f.Context(); if (human) context.Mode = "Human"; await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, context, default); };
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order });
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Turn("enviar pedido"));
        Assert.Empty(f.Orders); Assert.Empty(f.Sent); Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
    }
    [Theory]
    [InlineData(MessageKind.Text, false)] [InlineData(MessageKind.Document, false)]
    [InlineData(MessageKind.Text, true)] [InlineData(MessageKind.Document, true)]
    public async Task Human_takeover_suppresses_prepared_or_new_automatic_sends_without_a_document_fallback(MessageKind kind, bool prepareAfter)
    {
        var f = new OutboundFixture();
        if (prepareAfter) await f.Handoff();
        var message = f.Message(kind);
        await f.Repo.PrepareOutboundAsync(f.Workspace, "chat", "phone", message, default);
        if (!prepareAfter) await f.Handoff();
        Assert.True(await f.Service.TryResumeResponseAsync(f.Workspace, "source", default));
        var persisted = (await f.Repo.GetOutboundAsync(f.Workspace, message.Id, default))!.Value.Message;
        Assert.Equal(MessageStatus.Failed, persisted.Status); Assert.Equal(OutboundMessageService.HumanTakeoverReason, persisted.LastError);
        Assert.True(await f.Service.TryResumeResponseAsync(f.Workspace, "source", default)); Assert.Equal(0, f.Transports);
    }

    [Theory]
    [InlineData("ORDER")] [InlineData("RESERVATION")]
    public async Task Concurrent_context_writers_cannot_replace_a_newer_business_draft(string goal)
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationStateRepository(db.Db); var w = Guid.NewGuid();
        var original = new ConversationContextDto { CurrentGoal = goal }; await repo.UpsertStateAsync(w, "phone", original, default);
        var a = (await repo.GetStateAsync(w, "phone", default))!; var b = (await repo.GetStateAsync(w, "phone", default))!;
        a.TargetDate = "2026-10-20"; b.TargetDate = "2026-10-21";
        db.SynchronizeReads($"workspaces/{w}/conversationStates/phone");
        async Task<bool> Save(ConversationContextDto context)
        { try { await repo.UpsertStateAsync(w, "phone", context, default); return true; } catch (ConcurrencyException) { return false; } }
        var results = await Task.WhenAll(Save(a), Save(b)); Assert.Single(results, r => r);
        var final = (await repo.GetStateAsync(w, "phone", default))!; Assert.Equal(goal, final.CurrentGoal);
        Assert.Equal(results[0] ? a.TargetDate : b.TargetDate, final.TargetDate); Assert.True(db.AbortedCommits >= 1);
    }

    [Fact]
    public async Task Handoff_acknowledgement_is_durable_and_sent_once_while_normal_automatic_work_is_blocked()
    {
        var f = new OutboundFixture(); await f.Handoff();
        var first = await f.Service.SendHandoffAcknowledgementAsync(f.Workspace, "chat", "phone", "Te transfiero a un asesor.", "handoff", default);
        var second = await f.Service.SendHandoffAcknowledgementAsync(f.Workspace, "chat", "phone", "Te transfiero a un asesor.", "handoff", default);
        Assert.Equal(first.Id, second.Id); Assert.Equal(MessageStatus.Sent, second.Status); Assert.True(second.IsHandoffAcknowledgement); Assert.Equal(1, f.Transports);
    }

    [Fact]
    public async Task A_late_reservation_replay_never_erases_a_newer_draft()
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("RESERVATION");
        var context = await f.Context(); context.RecoveryLastMessageId = "old-confirmation";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, context, default);
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context()); var utc = new DateTime(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);
        f.Bookings.Add("old-confirmation", Reservation.Create(f.Workspace, "sede-a", "service-b", AssistantConversationTests.Fixture.Phone, "Ana Pérez", utc, utc.AddMinutes(30), "old-confirmation"));
        await f.Turn("sí", "old-confirmation");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task Repeated_confirmations_with_new_message_ids_never_create_a_second_reservation()
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("RESERVATION");
        var c = await f.Context(); c.TargetDate = "2026-10-20"; c.TargetTime = "10:00"; c.CurrentStep = "CONFIRM";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, c, default);
        await f.Turn("sí", "first"); await f.Turn("sí", "second"); await f.Turn("sí", "second");
        Assert.Single(f.Bookings); Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("PRODUCT")] [InlineData("SERVICE")]
    public async Task Concurrent_document_sends_claim_one_transport_and_keep_tenant_and_scope_identity(string scope)
    {
        var f = new OutboundFixture();
        Task<MessageRecord> Send() => f.Service.SendDocumentAsync(f.Workspace, "chat", "phone", "PDF", "https://example.test/catalog.pdf", "catalog.pdf", scope, "source", default);
        var results = await Task.WhenAll(Send(), Send()); Assert.Equal(results[0].Id, results[1].Id); Assert.Equal(1, f.Transports);
        var persisted = (await f.Repo.GetOutboundAsync(f.Workspace, results[0].Id, default))!.Value.Message;
        Assert.Equal(MessageStatus.Sent, persisted.Status); Assert.Contains(scope, persisted.IdempotencyKey);
        Assert.Null(await f.Repo.GetOutboundAsync(Guid.NewGuid(), results[0].Id, default));
    }

    [Fact]
    public async Task Order_commit_followed_by_context_failure_recovers_the_same_order_after_restart()
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("ORDER");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order });
        f.Firestore.BeforeCommit = commit => commit.Writes.Any(w => w.Update?.Fields.TryGetValue("contextJson", out var json) == true && json.StringValue.Contains("\"OrderLastSourceMessageId\":\"finish\""))
            ? new IOException("Crash after order persistence") : null;
        await Assert.ThrowsAsync<IOException>(() => f.Turn("enviar pedido", "finish")); Assert.Single(f.Orders); Assert.Empty(f.Sent);
        f.Firestore.BeforeCommit = null; f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order });
        await f.Turn("enviar pedido", "finish"); Assert.Single(f.Orders); Assert.Single(f.Sent); Assert.Empty((await f.Context()).OrderDraftItems);
    }

    private sealed class OutboundFixture
    {
        public Guid Workspace { get; } = Guid.NewGuid(); public InMemoryFirestore Db { get; } = new();
        public FirestoreConversationRepository Repo { get; } public OutboundMessageService Service { get; } public int Transports;
        public OutboundFixture()
        {
            AssistantRc1AdversarialTests.SeedConversation(Db, Workspace); Repo = new(Db.Db);
            var gateway = new Mock<IMessageGateway>(MockBehavior.Strict);
            gateway.Setup(g => g.SendTextAsync(Workspace, "phone", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid _, string _, string _, string _, Func<CancellationToken, Task> starting, CancellationToken ct) => { await starting(ct); return $"provider-{Interlocked.Increment(ref Transports)}"; });
            gateway.Setup(g => g.SendDocumentAsync(Workspace, "phone", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid _, string _, string _, string _, string _, string _, Func<CancellationToken, Task> starting, CancellationToken ct) => { await starting(ct); return $"provider-{Interlocked.Increment(ref Transports)}"; });
            Service = new(Repo, gateway.Object, Mock.Of<INotificationService>(), NullLogger<OutboundMessageService>.Instance);
        }
        public Task Handoff() => Repo.SetHandoffAsync(Workspace, "chat", "phone", ConversationMode.Human, HandoffReason.ManualIntervention, default);
        public MessageRecord Message(MessageKind kind)
        {
            var key = kind == MessageKind.Text ? $"{Workspace:N}:AI:source:response:1" : $"{Workspace:N}:AI:source:response:document:PRODUCT:1";
            return new() { Id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))), IdempotencyKey = key,
            Sender = SenderType.AI, Direction = "outbound", Kind = kind, Content = "Respuesta preparada", MediaUrl = kind == MessageKind.Document ? "https://example.test/catalog.pdf" : null,
            FileName = kind == MessageKind.Document ? "catalog.pdf" : null, Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow };
        }
    }
}
