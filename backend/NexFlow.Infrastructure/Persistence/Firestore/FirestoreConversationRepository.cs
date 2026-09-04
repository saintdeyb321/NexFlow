using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreConversationRepository : IConversationRepository
{
    private readonly FirestoreDb _db;

    public FirestoreConversationRepository(FirestoreDb db) => _db = db;

    private CollectionReference GetCollection(Guid workspaceId) =>
        _db.Collection("workspaces").Document(workspaceId.ToString()).Collection("conversations");

    public async Task<ConversationRecord?> GetActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken)
    {
        var query = GetCollection(workspaceId).WhereEqualTo("consumerPhone", consumerPhone).WhereEqualTo("status", "open").OrderByDescending("startedAt").Limit(1);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        var doc = snapshot.Documents.FirstOrDefault();
        return doc == null ? null : MapToConversation(doc);
    }

    public async Task<ConversationRecord> GetOrCreateActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken)
    {
        var collection = GetCollection(workspaceId);
        return await _db.RunTransactionAsync(async transaction =>
        {
            var query = collection.WhereEqualTo("consumerPhone", consumerPhone).WhereEqualTo("status", "open").OrderByDescending("startedAt").Limit(1);
            var snapshot = await transaction.GetSnapshotAsync(query, cancellationToken);
            var doc = snapshot.Documents.FirstOrDefault();
            if (doc != null) return MapToConversation(doc);

            var newConv = new ConversationRecord
            {
                Id = Guid.NewGuid().ToString(),
                ConsumerPhone = consumerPhone,
                Channel = "whatsapp",
                Mode = ConversationMode.Automatic,
                Status = "open",
                StartedAt = DateTime.UtcNow,
                LastMessageAt = DateTime.UtcNow,
                HandoffReason = HandoffReason.None
            };

            var expiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(90), DateTimeKind.Utc);
            var data = new Dictionary<string, object>
            {
                { "id", newConv.Id }, { "consumerPhone", newConv.ConsumerPhone }, { "channel", newConv.Channel },
                { "mode", newConv.Mode.ToString() }, { "status", newConv.Status },
                { "handoffReason", newConv.HandoffReason.ToString() },
                { "startedAt", DateTime.SpecifyKind(newConv.StartedAt, DateTimeKind.Utc) },
                { "lastMessageAt", DateTime.SpecifyKind(newConv.LastMessageAt, DateTimeKind.Utc) }, { "expiresAt", expiresAt }
            };
            transaction.Set(collection.Document(newConv.Id), data);
            return newConv;
        }, cancellationToken: cancellationToken);
    }

    public async Task CreateConversationAsync(Guid workspaceId, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        var expiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(90), DateTimeKind.Utc);
        var data = new Dictionary<string, object>
        {
            { "id", conversation.Id }, { "consumerPhone", conversation.ConsumerPhone }, { "channel", conversation.Channel },
            { "mode", conversation.Mode.ToString() }, { "status", conversation.Status },
            { "handoffReason", conversation.HandoffReason.ToString() },
            { "startedAt", DateTime.SpecifyKind(conversation.StartedAt, DateTimeKind.Utc) },
            { "lastMessageAt", DateTime.SpecifyKind(conversation.LastMessageAt, DateTimeKind.Utc) }, { "expiresAt", expiresAt }
        };
        await GetCollection(workspaceId).Document(conversation.Id).SetAsync(data, cancellationToken: cancellationToken);
    }

    public async Task UpdateConversationModeAsync(Guid workspaceId, string conversationId, ConversationMode mode, HandoffReason reason, CancellationToken cancellationToken)
    {
        var updates = new Dictionary<string, object> { { "mode", mode.ToString() }, { "handoffReason", reason.ToString() } };
        await GetCollection(workspaceId).Document(conversationId).UpdateAsync(updates, cancellationToken: cancellationToken);
    }

    public async Task AddMessageAsync(Guid workspaceId, string conversationId, MessageRecord message, CancellationToken cancellationToken)
    {
        var expiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(90), DateTimeKind.Utc);
        var data = new Dictionary<string, object>
        {
            { "id", message.Id }, { "direction", message.Direction }, { "sender", message.Sender.ToString() },
            { "content", message.Content }, { "status", message.Status.ToString() },
            { "timestamp", DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc) }, { "expiresAt", expiresAt }
        };
        if (!string.IsNullOrEmpty(message.ExternalMessageId)) data["externalMessageId"] = message.ExternalMessageId;

        await GetCollection(workspaceId).Document(conversationId).Collection("messages").Document(message.Id).SetAsync(data, cancellationToken: cancellationToken);
        await GetCollection(workspaceId).Document(conversationId).UpdateAsync(new Dictionary<string, object> { { "lastMessageAt", DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc) }, { "expiresAt", expiresAt } }, cancellationToken: cancellationToken);
    }

    public async Task UpdateMessageStatusAsync(Guid workspaceId, string conversationId, string messageId, MessageStatus status, string? externalMessageId, CancellationToken cancellationToken)
    {
        var updates = new Dictionary<string, object> { { "status", status.ToString() } };
        if (!string.IsNullOrEmpty(externalMessageId)) updates["externalMessageId"] = externalMessageId;
        await GetCollection(workspaceId).Document(conversationId).Collection("messages").Document(messageId).UpdateAsync(updates, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<ConversationRecord>> GetRecentConversationsAsync(Guid workspaceId, int limit, CancellationToken cancellationToken)
    {
        var query = GetCollection(workspaceId).OrderByDescending("lastMessageAt").Limit(limit);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToConversation);
    }

    public async Task<IEnumerable<MessageRecord>> GetMessagesAsync(Guid workspaceId, string conversationId, int limit, CancellationToken cancellationToken)
    {
        try
        {
            var query = GetCollection(workspaceId).Document(conversationId).Collection("messages").OrderByDescending("timestamp").Limit(limit);
            var snapshot = await query.GetSnapshotAsync(cancellationToken);
            return snapshot.Documents.Select(d => new MessageRecord
            {
                Id = d.Id,
                Direction = d.GetValue<string>("direction"),
                Sender = Enum.Parse<SenderType>(d.GetValue<string>("sender")),
                Content = d.GetValue<string>("content"),
                Status = d.TryGetValue("status", out string statusStr) && Enum.TryParse<MessageStatus>(statusStr, out var status) ? status : MessageStatus.Sent,
                ExternalMessageId = d.TryGetValue("externalMessageId", out string extId) ? extId : null,
                Timestamp = d.GetValue<Timestamp>("timestamp").ToDateTime()
            }).Reverse();
        }
        catch { return Enumerable.Empty<MessageRecord>(); }
    }

    private static ConversationRecord MapToConversation(DocumentSnapshot doc)
    {
        var record = new ConversationRecord
        {
            Id = doc.Id,
            ConsumerPhone = doc.GetValue<string>("consumerPhone"),
            Channel = doc.GetValue<string>("channel"),
            Mode = Enum.Parse<ConversationMode>(doc.GetValue<string>("mode")),
            Status = doc.GetValue<string>("status"),
            StartedAt = doc.GetValue<Timestamp>("startedAt").ToDateTime(),
            LastMessageAt = doc.GetValue<Timestamp>("lastMessageAt").ToDateTime()
        };
        if (doc.TryGetValue("handoffReason", out string reasonStr) && Enum.TryParse<HandoffReason>(reasonStr, out var reason))
            record = record with { HandoffReason = reason };
        return record;
    }

    public async Task<ConversationRecord?> GetConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(conversationId);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        return !snapshot.Exists ? null : MapToConversation(snapshot);
    }

    public async Task DeleteConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken)
    {
        // 🔥 SPRINT 1.3: Limpieza síncrona en Lotes (Bulk Write) para evitar datos huérfanos.
        var convRef = GetCollection(workspaceId).Document(conversationId);
        var messagesSnapshot = await convRef.Collection("messages").GetSnapshotAsync(cancellationToken);

        var batch = _db.StartBatch();

        foreach (var messageDoc in messagesSnapshot.Documents)
        {
            batch.Delete(messageDoc.Reference);
        }
        batch.Delete(convRef);

        await batch.CommitAsync(cancellationToken);
    }
}