using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreConversationRepository : IConversationRepository
{
    private readonly FirestoreDb _db;

    public FirestoreConversationRepository(FirestoreDb db) => _db = db;

    private CollectionReference GetCollection(Guid workspaceId) =>
        _db.Collection("workspaces").Document(workspaceId.ToString()).Collection("conversations");

    private CollectionReference GetOutboundCollection(Guid workspaceId) =>
        _db.Collection("workspaces").Document(workspaceId.ToString()).Collection("outboundMessages");

    private DocumentReference GetOriginDocument(Guid workspaceId, string phone, string externalId) =>
        _db.Collection("workspaces").Document(workspaceId.ToString()).Collection("messageOrigins")
            .Document(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{phone}:{externalId}"))));

    public async Task<(MessageRecord Message, bool SendRequired)> PrepareOutboundAsync(
        Guid workspaceId, string conversationId, string phone, MessageRecord message, CancellationToken cancellationToken)
    {
        var outboundRef = GetOutboundCollection(workspaceId).Document(message.Id);
        var conversationRef = GetCollection(workspaceId).Document(conversationId);
        return await _db.RunTransactionAsync(async transaction =>
        {
            var existing = await transaction.GetSnapshotAsync(outboundRef, cancellationToken);
            var conversation = await transaction.GetSnapshotAsync(conversationRef, cancellationToken);
            if (!conversation.Exists || conversation.GetValue<string>("consumerPhone") != phone)
                throw new InvalidOperationException("Outbound conversation/recipient mismatch.");

            if (existing.Exists)
            {
                if (existing.GetValue<string>("conversationId") != conversationId ||
                    existing.GetValue<string>("phone") != phone ||
                    existing.GetValue<string>("idempotencyKey") != message.IdempotencyKey)
                    throw new InvalidOperationException("Outbound idempotency key conflict.");
                var persisted = MapMessage(existing);
                if (persisted.Sender != message.Sender ||
                    (message.Sender == SenderType.BusinessUser && persisted.Content != message.Content))
                    throw new InvalidOperationException("Outbound idempotency key reused for different content.");
                if (persisted.Status != MessageStatus.Failed) return (persisted, false);
                message = persisted with { Status = MessageStatus.Pending, LastError = null };
            }

            var data = MessageData(message);
            data["conversationId"] = conversationId;
            data["phone"] = phone;
            data["attemptedAt"] = DateTime.UtcNow;
            // This ledger has no history TTL: deleting/expiring a conversation must
            // not enable a resend or erase provider-origin evidence.
            transaction.Set(outboundRef, data);
            transaction.Set(conversationRef.Collection("messages").Document(message.Id), MessageData(message));
            return (message, true);
        }, cancellationToken: cancellationToken);
    }

    public async Task ConfirmOutboundAsync(Guid workspaceId, string messageId, string externalMessageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalMessageId)) throw new ArgumentException("Provider ID is required.");
        var outboundRef = GetOutboundCollection(workspaceId).Document(messageId);
        await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(outboundRef, cancellationToken);
            if (!snapshot.Exists) throw new InvalidOperationException("Outbound attempt not found.");
            var message = MapMessage(snapshot);
            if (message.ExternalMessageId != null && message.ExternalMessageId != externalMessageId)
                throw new InvalidOperationException("Outbound already has a different provider ID.");
            var phone = snapshot.GetValue<string>("phone");
            var originRef = GetOriginDocument(workspaceId, phone, externalMessageId);
            var origin = await transaction.GetSnapshotAsync(originRef, cancellationToken);
            if (origin.Exists && origin.GetValue<string>("messageId") != messageId)
                throw new InvalidOperationException("Provider ID is already assigned to another outbound.");
            var conversationRef = GetCollection(workspaceId).Document(snapshot.GetValue<string>("conversationId"));
            var conversation = await transaction.GetSnapshotAsync(conversationRef, cancellationToken);
            var updates = new Dictionary<string, object>
            {
                ["status"] = MessageStatus.Sent.ToString(), ["externalMessageId"] = externalMessageId, ["lastError"] = FieldValue.Delete
            };
            transaction.Update(outboundRef, updates);
            transaction.Set(originRef, new Dictionary<string, object>
            {
                ["origin"] = message.Origin!.Value.ToString(), ["messageId"] = messageId,
                ["externalMessageId"] = externalMessageId, ["phone"] = phone
            });
            if (conversation.Exists)
            {
                transaction.Set(conversationRef.Collection("messages").Document(messageId),
                    MessageData(message with { Status = MessageStatus.Sent, ExternalMessageId = externalMessageId, LastError = null }));
                transaction.Update(conversationRef, new Dictionary<string, object> { ["lastMessageAt"] = DateTime.UtcNow });
            }
        }, cancellationToken: cancellationToken);
    }

    public async Task RecordOutboundFailureAsync(Guid workspaceId, string messageId, string error, bool rejected, CancellationToken cancellationToken)
    {
        var outboundRef = GetOutboundCollection(workspaceId).Document(messageId);
        await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(outboundRef, cancellationToken);
            if (!snapshot.Exists) throw new InvalidOperationException("Outbound attempt not found.");
            if (MapMessage(snapshot).Status == MessageStatus.Sent) return;
            var conversationRef = GetCollection(workspaceId).Document(snapshot.GetValue<string>("conversationId"));
            var conversation = await transaction.GetSnapshotAsync(conversationRef, cancellationToken);
            var status = rejected ? MessageStatus.Failed : MessageStatus.Pending;
            var updates = new Dictionary<string, object> { ["status"] = status.ToString(), ["lastError"] = error };
            transaction.Update(outboundRef, updates);
            if (conversation.Exists)
                transaction.Set(conversationRef.Collection("messages").Document(messageId),
                    MessageData(MapMessage(snapshot) with { Status = status, LastError = error }));
        }, cancellationToken: cancellationToken);
    }

    public async Task<(MessageRecord Message, string ConversationId, string Phone)?> GetOutboundAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var snapshot = await GetOutboundCollection(workspaceId).Document(messageId).GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? (MapMessage(snapshot), snapshot.GetValue<string>("conversationId"), snapshot.GetValue<string>("phone")) : null;
    }
    public async Task<MessageOrigin?> GetMessageOriginAsync(Guid workspaceId, string phone, string externalMessageId, CancellationToken cancellationToken)
    {
        var snapshot = await GetOriginDocument(workspaceId, phone, externalMessageId).GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? Enum.Parse<MessageOrigin>(snapshot.GetValue<string>("origin")) : null;
    }

    public async Task<bool> HasUnconfirmedOutboundAsync(Guid workspaceId, string phone, CancellationToken cancellationToken)
    {
        var snapshot = await GetOutboundCollection(workspaceId).WhereEqualTo("phone", phone)
            .WhereEqualTo("status", MessageStatus.Pending.ToString()).Limit(1).GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Count != 0;
    }

    private static Dictionary<string, object> MessageData(MessageRecord message)
    {
        var data = new Dictionary<string, object>
        {
            ["id"] = message.Id, ["direction"] = message.Direction, ["sender"] = message.Sender.ToString(),
            ["content"] = message.Content, ["status"] = message.Status.ToString(), ["timestamp"] = message.Timestamp
        };
        if (message.ExternalMessageId != null) data["externalMessageId"] = message.ExternalMessageId;
        if (message.IdempotencyKey != null) data["idempotencyKey"] = message.IdempotencyKey;
        if (message.LastError != null) data["lastError"] = message.LastError;
        if (message.Origin.HasValue) data["origin"] = message.Origin.Value.ToString();
        return data;
    }

    private static MessageRecord MapMessage(DocumentSnapshot doc) => new()
    {
        Id = doc.Id,
        Direction = doc.GetValue<string>("direction"),
        Sender = Enum.Parse<SenderType>(doc.GetValue<string>("sender")),
        Content = doc.GetValue<string>("content"),
        Status = doc.TryGetValue("status", out string statusText) && Enum.TryParse<MessageStatus>(statusText, out var status) ? status : MessageStatus.Sent,
        ExternalMessageId = doc.TryGetValue("externalMessageId", out string externalId) ? externalId : null,
        LastError = doc.TryGetValue("lastError", out string lastError) ? lastError : null,
        IdempotencyKey = doc.TryGetValue("idempotencyKey", out string key) ? key : null,
        Origin = doc.TryGetValue("origin", out string origin) ? Enum.Parse<MessageOrigin>(origin) : null,
        Timestamp = doc.GetValue<Timestamp>("timestamp").ToDateTime()
    };
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
        var stateLock = ConversationStateService.GetStateLock(workspaceId, conversationId);
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            var updates = new Dictionary<string, object> { { "mode", mode.ToString() }, { "handoffReason", reason.ToString() } };
            await GetCollection(workspaceId).Document(conversationId).UpdateAsync(updates, cancellationToken: cancellationToken);
        }
        finally
        {
            stateLock.Release();
        }
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
        if (message.Origin.HasValue) data["origin"] = message.Origin.Value.ToString();
        if (message.IdempotencyKey != null) data["idempotencyKey"] = message.IdempotencyKey;
        if (message.LastError != null) data["lastError"] = message.LastError;
        if (!string.IsNullOrEmpty(message.ExternalMessageId)) data["externalMessageId"] = message.ExternalMessageId;

        await GetCollection(workspaceId).Document(conversationId).Collection("messages").Document(message.Id).SetAsync(data, cancellationToken: cancellationToken);
        await GetCollection(workspaceId).Document(conversationId).UpdateAsync(new Dictionary<string, object> { { "lastMessageAt", DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc) }, { "expiresAt", expiresAt } }, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<ConversationRecord>> GetRecentConversationsAsync(Guid workspaceId, int limit, CancellationToken cancellationToken)
    {
        var query = GetCollection(workspaceId).OrderByDescending("lastMessageAt").Limit(limit);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToConversation);
    }

    public async Task<IEnumerable<MessageRecord>> GetMessagesAsync(Guid workspaceId, string conversationId, int limit, CancellationToken cancellationToken)
    {
        var query = GetCollection(workspaceId).Document(conversationId).Collection("messages").OrderByDescending("timestamp").Limit(limit);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Select(MapMessage).Reverse();
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

    // 🔥 SPRINT 03: Búsqueda precisa por conversationId en lugar de CollectionGroup global
    public async Task<MessageRecord?> GetMessageByExternalIdAsync(Guid workspaceId, string conversationId, string externalMessageId, CancellationToken cancellationToken)
    {
        var msgQuery = GetCollection(workspaceId)
            .Document(conversationId)
            .Collection("messages")
            .WhereEqualTo("externalMessageId", externalMessageId)
            .Limit(1);

        var msgSnapshot = await msgQuery.GetSnapshotAsync(cancellationToken);
        var msgDoc = msgSnapshot.Documents.FirstOrDefault();

        return msgDoc == null ? null : MapMessage(msgDoc);
    }

    public async Task DeleteConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken)
    {
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

    public async Task CloseConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(conversationId);
        await docRef.UpdateAsync(new Dictionary<string, object> { { "status", "closed" } }, cancellationToken: cancellationToken);
    }
}