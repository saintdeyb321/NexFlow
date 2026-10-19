using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3Sprint2ActivityTests
{
    [Fact]
    public async Task Repeated_creation_preserves_the_existing_utc_normalization_contract()
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(db.Db); var w = Guid.NewGuid(); var now = DateTime.UtcNow;
        await repo.CreateConversationAsync(w, Conversation("a", now), default);
        await repo.CreateConversationAsync(w, Conversation("a", DateTime.SpecifyKind(now.AddMinutes(1), DateTimeKind.Unspecified)), default);
        Assert.Equal(now.AddMinutes(1), (await repo.GetConversationAsync(w, "a", default))!.LastMessageAt);
    }
    [Fact]
    public async Task Old_message_and_conversation_replay_cannot_move_activity_backwards_and_ttl_survives()
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(db.Db); var w = Guid.NewGuid(); var now = DateTime.UtcNow;
        var conversation = Conversation("a", now.AddHours(-1)); await repo.CreateConversationAsync(w, conversation, default);
        await repo.AddMessageAsync(w, "a", Message("new", now), default);
        await repo.AddMessageAsync(w, "a", Message("old", now.AddHours(-2)), default);
        await repo.CreateConversationAsync(w, conversation, default);
        Assert.Equal(now, (await repo.GetConversationAsync(w, "a", default))!.LastMessageAt);
        var messages = (await repo.GetMessagesAsync(w, "a", 50, default)).ToArray();
        Assert.Equal(new[] { "old", "new" }, messages.Select(m => m.Id)); Assert.Equal(now.AddHours(-2), messages[0].Timestamp);
        Assert.True(db.Read($"workspaces/{w}/conversations/a")!.Fields["expiresAt"].TimestampValue.ToDateTime() > now.AddDays(89));
        Assert.True(db.Read($"workspaces/{w}/conversations/a/messages/old")!.Fields.ContainsKey("expiresAt"));
    }

    [Fact]
    public async Task Concurrent_out_of_order_writers_retry_atomically_and_preserve_both_messages()
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(db.Db); var w = Guid.NewGuid(); var now = DateTime.UtcNow;
        await repo.CreateConversationAsync(w, Conversation("a", now.AddHours(-2)), default);
        db.SynchronizeReads($"workspaces/{w}/conversations/a");
        await Task.WhenAll(repo.AddMessageAsync(w, "a", Message("new", now), default), repo.AddMessageAsync(w, "a", Message("old", now.AddHours(-1)), default));
        Assert.True(db.AbortedCommits > 0); Assert.Equal(now, (await repo.GetConversationAsync(w, "a", default))!.LastMessageAt);
        Assert.Equal(2, (await repo.GetMessagesAsync(w, "a", 50, default)).Count());
    }

    [Fact]
    public async Task Conversations_workspaces_and_incremental_cursors_remain_isolated_and_ordered()
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(db.Db); var a = Guid.NewGuid(); var b = Guid.NewGuid(); var now = DateTime.UtcNow;
        await repo.CreateConversationAsync(a, Conversation("a", now), default); await repo.CreateConversationAsync(a, Conversation("b", now), default);
        await repo.CreateConversationAsync(b, Conversation("a", now.AddHours(-5)), default);
        await repo.AddMessageAsync(a, "a", Message("new-a", now.AddMinutes(1)), default);
        await repo.AddMessageAsync(a, "b", Message("new-b", now.AddMinutes(2)), default);
        await repo.AddMessageAsync(a, "a", Message("old", now.AddHours(-1)), default);
        Assert.Equal(now.AddHours(-5), (await repo.GetConversationAsync(b, "a", default))!.LastMessageAt);
        Assert.Equal(new[] { "b", "a" }, (await repo.GetRecentConversationsAsync(a, 50, default)).Select(c => c.Id));
        var first = Assert.Single(await repo.GetRecentConversationsAsync(a, 1, default, now));
        Assert.Equal("a", first.Id);
        Assert.Equal("b", Assert.Single(await repo.GetRecentConversationsAsync(a, 1, default, first.LastMessageAt, first.Id)).Id);
        Assert.Equal("new-a", Assert.Single(await repo.GetMessagesAsync(a, "a", 1, default, now)).Id);
    }

    [Fact]
    public async Task Provider_confirmation_and_late_echo_never_regress_a_newer_activity_or_change_the_original_timestamp()
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(db.Db); var w = Guid.NewGuid(); var now = DateTime.UtcNow;
        await repo.CreateConversationAsync(w, Conversation("a", now), default);
        var response = Message("response", now) with { Sender = SenderType.AI, Direction = "outbound", IdempotencyKey = "key", Status = MessageStatus.Pending };
        await repo.PrepareOutboundAsync(w, "a", "phone", response, default); Assert.True(await repo.StartOutboundAsync(w, "response", default));
        var newer = now.AddDays(1); await repo.AddMessageAsync(w, "a", Message("newest", newer), default);
        await repo.ConfirmOutboundAsync(w, "response", "provider", default);
        await repo.AddMessageAsync(w, "a", Message("echo", now.AddHours(-1)), default);
        await repo.ConfirmOutboundAsync(w, "response", "provider", default);
        Assert.Equal(newer, (await repo.GetConversationAsync(w, "a", default))!.LastMessageAt);
        var outbound = (await repo.GetOutboundAsync(w, "response", default))!.Value.Message;
        Assert.Equal(now, outbound.Timestamp); Assert.Equal(MessageStatus.Sent, outbound.Status);
    }

    [Fact]
    public async Task Equal_timestamp_cursors_use_document_identity_without_missing_or_repeating_records()
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(db.Db); var w = Guid.NewGuid(); var now = DateTime.UtcNow;
        await repo.CreateConversationAsync(w, Conversation("a", now), default); await repo.CreateConversationAsync(w, Conversation("b", now), default);
        var first = Assert.Single(await repo.GetRecentConversationsAsync(w, 1, default, now)); Assert.Equal("a", first.Id);
        Assert.Equal("b", Assert.Single(await repo.GetRecentConversationsAsync(w, 1, default, now, first.Id)).Id);
        await repo.AddMessageAsync(w, "a", Message("a", now), default); await repo.AddMessageAsync(w, "a", Message("b", now), default);
        Assert.Equal("a", Assert.Single(await repo.GetMessagesAsync(w, "a", 1, default, now)).Id);
        Assert.Equal("b", Assert.Single(await repo.GetMessagesAsync(w, "a", 1, default, now, "a")).Id);
    }

    [Fact]
    public async Task Failed_commit_cannot_persist_a_message_without_its_activity_update()
    {
        var db = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(db.Db); var w = Guid.NewGuid(); var now = DateTime.UtcNow;
        await repo.CreateConversationAsync(w, Conversation("a", now), default);
        db.BeforeCommit = _ => new IOException("Simulated commit failure");
        await Assert.ThrowsAsync<IOException>(() => repo.AddMessageAsync(w, "a", Message("new", now.AddMinutes(1)), default));
        db.BeforeCommit = null;
        Assert.Null(db.Read($"workspaces/{w}/conversations/a/messages/new"));
        Assert.Equal(now, (await repo.GetConversationAsync(w, "a", default))!.LastMessageAt);
    }

    private static ConversationRecord Conversation(string id, DateTime time) => new() { Id = id, ConsumerPhone = "phone", Channel = "WhatsApp", Status = "open", Mode = ConversationMode.Automatic, StartedAt = time, LastMessageAt = time };
    private static MessageRecord Message(string id, DateTime time) => new() { Id = id, Timestamp = time, Content = "Fictitious", Sender = SenderType.Consumer, Direction = "inbound", Status = MessageStatus.Sent };
}
