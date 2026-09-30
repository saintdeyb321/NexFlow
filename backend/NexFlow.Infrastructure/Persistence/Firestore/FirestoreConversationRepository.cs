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

    private static readonly TimeSpan TransportLease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan EchoWindow = TimeSpan.FromMinutes(10);

    private static MessageRecord NormalizeAttempt(DocumentSnapshot snapshot)
    {
        var message = MapMessage(snapshot);
        // Old Pending records were written before POST with attemptedAt. Their
        // delivery cannot be proved; never turn them into a new transport attempt.
        var legacyAttempt = message.Status == MessageStatus.Pending && snapshot.ContainsField("attemptedAt");
        var expired = message.Status == MessageStatus.Attempting &&
            (!message.TransportLeaseUntil.HasValue || message.TransportLeaseUntil <= DateTime.UtcNow);
        return legacyAttempt || expired
            ? message with { Status = MessageStatus.UnknownDelivery, TransportLeaseUntil = null,
                LastError = message.LastError ?? "Transport ended without durable provider confirmation." }
            : message;
    }

    private static void WriteMessageState(Transaction transaction, DocumentSnapshot outbound,
        DocumentSnapshot conversation, MessageRecord message)
    {
        transaction.Update(outbound.Reference, new Dictionary<string, object>
        {
            ["status"] = message.Status.ToString(),
            ["lastError"] = message.LastError is null ? FieldValue.Delete : message.LastError,
            ["transportLeaseUntil"] = message.TransportLeaseUntil.HasValue ? message.TransportLeaseUntil.Value : FieldValue.Delete
        });
        if (conversation.Exists)
            transaction.Set(conversation.Reference.Collection("messages").Document(message.Id), MessageData(message));
    }

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
                var persisted = NormalizeAttempt(existing);
                if (persisted.Sender != message.Sender ||
                    (message.Sender == SenderType.BusinessUser && persisted.Content != message.Content))
                    throw new InvalidOperationException("Outbound idempotency key reused for different content.");
                if (persisted.Status != MapMessage(existing).Status)
                    WriteMessageState(transaction, existing, conversation, persisted);
                return (persisted, persisted.Status == MessageStatus.Pending);
            }
            var data = MessageData(message);
            data["conversationId"] = conversationId;
            data["phone"] = phone;
            // Pending means prepared, never attempted. The separate claim is
            // committed immediately before the gateway invokes POST.
            transaction.Set(outboundRef, data);
            transaction.Set(conversationRef.Collection("messages").Document(message.Id), MessageData(message));
            return (message, true);
        }, cancellationToken: cancellationToken);
    }

    public async Task<bool> StartOutboundAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var outboundRef = GetOutboundCollection(workspaceId).Document(messageId);
        return await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(outboundRef, cancellationToken);
            if (!snapshot.Exists) throw new KeyNotFoundException("Outbound not found.");
            var conversationRef = GetCollection(workspaceId).Document(snapshot.GetValue<string>("conversationId"));
            var conversation = await transaction.GetSnapshotAsync(conversationRef, cancellationToken);
            var message = NormalizeAttempt(snapshot);
            if (message.Status != MessageStatus.Pending)
            {
                if (message.Status != MapMessage(snapshot).Status) WriteMessageState(transaction, snapshot, conversation, message);
                return false;
            }
            if (!conversation.Exists || conversation.GetValue<string>("consumerPhone") != snapshot.GetValue<string>("phone"))
                throw new KeyNotFoundException("Outbound conversation not found.");
            var startedAt = DateTime.UtcNow;
            message = message with { Status = MessageStatus.Attempting, TransportStartedAt = startedAt,
                TransportLeaseUntil = startedAt.Add(TransportLease), LastError = null };
            WriteMessageState(transaction, snapshot, conversation, message);
            transaction.Update(outboundRef, new Dictionary<string, object>
            {
                ["transportStartedAt"] = startedAt, ["attemptedAt"] = startedAt
            });
            return true;
        }, cancellationToken: cancellationToken);
    }

    private async Task<MessageOrigin> ConfirmInTransactionAsync(Transaction transaction, Guid workspaceId,
        DocumentSnapshot snapshot, string externalMessageId, CancellationToken cancellationToken)
    {
        var message = MapMessage(snapshot);
        if (message.ExternalMessageId != null && message.ExternalMessageId != externalMessageId)
            throw new InvalidOperationException("Outbound already has a different provider ID.");
        if (message.Status == MessageStatus.Pending && !snapshot.ContainsField("attemptedAt"))
            throw new InvalidOperationException("Outbound transport has not started.");
        var phone = snapshot.GetValue<string>("phone");
        var originRef = GetOriginDocument(workspaceId, phone, externalMessageId);
        var origin = await transaction.GetSnapshotAsync(originRef, cancellationToken);
        if (origin.Exists && origin.GetValue<string>("messageId") != message.Id)
            throw new InvalidOperationException("Provider ID is already assigned to another outbound.");
        if (message.ProviderConfirmationId != null && message.ProviderConfirmationId != externalMessageId)
            throw new InvalidOperationException("Provider confirmation does not match this echo.");
        if (message.Status == MessageStatus.Sent && origin.Exists)
            return Enum.Parse<MessageOrigin>(origin.GetValue<string>("origin"));
        var conversationRef = GetCollection(workspaceId).Document(snapshot.GetValue<string>("conversationId"));
        var conversation = await transaction.GetSnapshotAsync(conversationRef, cancellationToken);
        var persistedOrigin = message.Origin ?? (message.Sender == SenderType.AI ? MessageOrigin.NexFlowAI : MessageOrigin.NexFlowHuman);
        var confirmed = message with { Status = MessageStatus.Sent, ExternalMessageId = externalMessageId,
            ProviderConfirmationId = externalMessageId, Origin = persistedOrigin, LastError = null, TransportLeaseUntil = null };
        WriteMessageState(transaction, snapshot, conversation, confirmed);
        transaction.Update(snapshot.Reference, new Dictionary<string, object>
        {
            ["externalMessageId"] = externalMessageId, ["providerConfirmationId"] = externalMessageId, ["origin"] = persistedOrigin.ToString()
        });
        transaction.Set(originRef, new Dictionary<string, object>
        {
            ["origin"] = persistedOrigin.ToString(), ["messageId"] = message.Id,
            ["externalMessageId"] = externalMessageId, ["phone"] = phone
        });
        if (conversation.Exists)
            transaction.Update(conversationRef, new Dictionary<string, object> { ["lastMessageAt"] = DateTime.UtcNow });
        return persistedOrigin;
    }

    public async Task ConfirmOutboundAsync(Guid workspaceId, string messageId, string externalMessageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalMessageId);
        await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(GetOutboundCollection(workspaceId).Document(messageId), cancellationToken);
            if (!snapshot.Exists) throw new KeyNotFoundException("Outbound attempt not found.");
            return await ConfirmInTransactionAsync(transaction, workspaceId, snapshot, externalMessageId, cancellationToken);
        }, cancellationToken: cancellationToken);
    }

    public async Task RecordOutboundFailureAsync(Guid workspaceId, string messageId, string error, MessageStatus status, CancellationToken cancellationToken, string? providerConfirmationId = null)
    {
        if (status is not (MessageStatus.Pending or MessageStatus.Failed or MessageStatus.UnknownDelivery))
            throw new ArgumentException("Invalid outbound failure status.");
        await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(GetOutboundCollection(workspaceId).Document(messageId), cancellationToken);
            if (!snapshot.Exists) throw new KeyNotFoundException("Outbound attempt not found.");
            var current = NormalizeAttempt(snapshot);
            if (current.Status is MessageStatus.Sent or MessageStatus.Failed) return;
            var conversationRef = GetCollection(workspaceId).Document(snapshot.GetValue<string>("conversationId"));
            var conversation = await transaction.GetSnapshotAsync(conversationRef, cancellationToken);
            if (current.ProviderConfirmationId != null && providerConfirmationId != null && current.ProviderConfirmationId != providerConfirmationId)
                throw new InvalidOperationException("Conflicting provider confirmation.");
            // A pre-POST exception may race a different caller's transport claim.
            // It can never reset that claim to a retryable Pending state.
            var resolved = status == MessageStatus.Pending && current.Status != MessageStatus.Pending
                ? MessageStatus.UnknownDelivery : status;
            WriteMessageState(transaction, snapshot, conversation, current with
            { Status = resolved, LastError = error, TransportLeaseUntil = null,
                ProviderConfirmationId = providerConfirmationId ?? current.ProviderConfirmationId });
            if (providerConfirmationId != null)
                transaction.Update(snapshot.Reference, new Dictionary<string, object> { ["providerConfirmationId"] = providerConfirmationId });
        }, cancellationToken: cancellationToken);
    }

    public async Task<(MessageRecord Message, string ConversationId, string Phone)?> GetOutboundAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        return await _db.RunTransactionAsync<(MessageRecord, string, string)?>(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(GetOutboundCollection(workspaceId).Document(messageId), cancellationToken);
            if (!snapshot.Exists) return null;
            var message = NormalizeAttempt(snapshot);
            if (message.Status != MapMessage(snapshot).Status)
            {
                var conversation = await transaction.GetSnapshotAsync(GetCollection(workspaceId).Document(snapshot.GetValue<string>("conversationId")), cancellationToken);
                WriteMessageState(transaction, snapshot, conversation, message);
            }
            return (message, snapshot.GetValue<string>("conversationId"), snapshot.GetValue<string>("phone"));
        }, cancellationToken: cancellationToken);
    }

    public async Task<MessageOrigin?> GetMessageOriginAsync(Guid workspaceId, string phone, string externalMessageId, CancellationToken cancellationToken)
    {
        var snapshot = await GetOriginDocument(workspaceId, phone, externalMessageId).GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? Enum.Parse<MessageOrigin>(snapshot.GetValue<string>("origin")) : null;
    }

    public async Task<OutboundReconciliationResult> ReconcileOutboundEchoAsync(Guid workspaceId, string? conversationId,
        string phone, string content, string externalMessageId, DateTime observedAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalMessageId);
        return await _db.RunTransactionAsync(async transaction =>
        {
            var origin = await transaction.GetSnapshotAsync(GetOriginDocument(workspaceId, phone, externalMessageId), cancellationToken);
            if (origin.Exists) return new OutboundReconciliationResult(Enum.Parse<MessageOrigin>(origin.GetValue<string>("origin")), false);
            // Read only unresolved attempts for this recipient. Compare the full
            // content locally; Firestore indexes truncate long string values.
            var documents = new List<DocumentSnapshot>();
            foreach (var status in new[] { MessageStatus.Attempting, MessageStatus.UnknownDelivery, MessageStatus.Pending })
            {
                var query = GetOutboundCollection(workspaceId).WhereEqualTo("phone", phone).WhereEqualTo("status", status.ToString());
                var attempts = await transaction.GetSnapshotAsync(query, cancellationToken);
                documents.AddRange(attempts.Documents);
            }
            var uncertain = documents.Where(d =>
            {
                var message = NormalizeAttempt(d);
                return message.Content == content && message.Direction == "outbound" && message.ExternalMessageId == null &&
                    message.Status is MessageStatus.Attempting or MessageStatus.UnknownDelivery;
            }).ToList();
            var candidates = uncertain.Where(d =>
            {
                var message = MapMessage(d);
                return (conversationId == null || d.GetValue<string>("conversationId") == conversationId) &&
                    (message.ProviderConfirmationId == null || message.ProviderConfirmationId == externalMessageId) &&
                    message.TransportStartedAt.HasValue &&
                    (message.ProviderConfirmationId == externalMessageId ||
                        (message.TransportStartedAt >= observedAt.Subtract(EchoWindow) &&
                         message.TransportStartedAt <= observedAt.AddSeconds(30)));
            }).ToList();
            if (candidates.Count != 1)
                return new OutboundReconciliationResult(null, uncertain.Count != 0);
            var confirmedOrigin = await ConfirmInTransactionAsync(transaction, workspaceId, candidates[0], externalMessageId, cancellationToken);
            return new OutboundReconciliationResult(confirmedOrigin, false);
        }, cancellationToken: cancellationToken);
    }

    public async Task<bool> HasUnconfirmedOutboundAsync(Guid workspaceId, string phone, CancellationToken cancellationToken)
    {
        // UnknownDelivery is finalized for inbound processing, not a permanent
        // conversation lock. Expired claims become explicit uncertainty on read.
        var snapshot = await GetOutboundCollection(workspaceId).WhereEqualTo("phone", phone)
            .WhereEqualTo("status", MessageStatus.Attempting.ToString()).GetSnapshotAsync(cancellationToken);
        foreach (var document in snapshot.Documents)
        {
            var outbound = await GetOutboundAsync(workspaceId, document.Id, cancellationToken);
            if (outbound?.Message.Status == MessageStatus.Attempting) return true;
        }
        return false;
    }

    private static Dictionary<string, object> MessageData(MessageRecord message)
    {
        var data = new Dictionary<string, object>
        {
            ["id"] = message.Id, ["direction"] = message.Direction, ["sender"] = message.Sender.ToString(),
            ["content"] = message.Content, ["status"] = message.Status.ToString(), ["timestamp"] = message.Timestamp
        };
        if (message.ExternalMessageId != null) data["externalMessageId"] = message.ExternalMessageId;
        if (message.ProviderConfirmationId != null) data["providerConfirmationId"] = message.ProviderConfirmationId;
        if (message.IdempotencyKey != null) data["idempotencyKey"] = message.IdempotencyKey;
        if (message.LastError != null) data["lastError"] = message.LastError;
        if (message.Origin.HasValue) data["origin"] = message.Origin.Value.ToString();
        if (message.TransportStartedAt.HasValue) data["transportStartedAt"] = message.TransportStartedAt.Value;
        if (message.TransportLeaseUntil.HasValue) data["transportLeaseUntil"] = message.TransportLeaseUntil.Value;
        return data;
    }

    private static MessageRecord MapMessage(DocumentSnapshot doc) => new()
    {
        Id = doc.Id,
        Direction = doc.GetValue<string>("direction"),
        Sender = Enum.Parse<SenderType>(doc.GetValue<string>("sender")),
        Content = doc.GetValue<string>("content"),
        Status = doc.TryGetValue("status", out string statusText) && Enum.TryParse<MessageStatus>(statusText, out var status) ? status : MessageStatus.UnknownDelivery,
        ExternalMessageId = doc.TryGetValue("externalMessageId", out string externalId) ? externalId : null,
        ProviderConfirmationId = doc.TryGetValue("providerConfirmationId", out string confirmationId) ? confirmationId : null,
        LastError = doc.TryGetValue("lastError", out string lastError) ? lastError : null,
        IdempotencyKey = doc.TryGetValue("idempotencyKey", out string key) ? key : null,
        Origin = doc.TryGetValue("origin", out string origin) ? Enum.Parse<MessageOrigin>(origin) : null,
        TransportStartedAt = doc.TryGetValue("transportStartedAt", out Timestamp startedAt) ? startedAt.ToDateTime()
            : doc.TryGetValue("attemptedAt", out Timestamp legacyStartedAt) ? legacyStartedAt.ToDateTime() : null,
        TransportLeaseUntil = doc.TryGetValue("transportLeaseUntil", out Timestamp leaseUntil) ? leaseUntil.ToDateTime() : null,
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

    public async Task SetHandoffAsync(Guid workspaceId, string conversationId, string phone, ConversationMode mode, HandoffReason reason, CancellationToken cancellationToken)
    {
        var conversationRef = GetCollection(workspaceId).Document(conversationId);
        var stateRef = _db.Collection("workspaces").Document(workspaceId.ToString()).Collection("conversationStates").Document(phone);
        await _db.RunTransactionAsync(async transaction =>
        {
            var conversation = await transaction.GetSnapshotAsync(conversationRef, cancellationToken);
            var state = await transaction.GetSnapshotAsync(stateRef, cancellationToken);
            if (!conversation.Exists || conversation.GetValue<string>("consumerPhone") != phone)
                throw new KeyNotFoundException("Conversation not found.");
            var context = state.Exists
                ? System.Text.Json.JsonSerializer.Deserialize<NexFlow.Application.Abstractions.Cache.ConversationContextDto>(state.GetValue<string>("contextJson"))
                    ?? throw new InvalidOperationException("Persisted conversation state is invalid.")
                : new NexFlow.Application.Abstractions.Cache.ConversationContextDto();
            context.Mode = mode.ToString();
            context.HandoffReason = mode == ConversationMode.Human ? reason.ToString() : null;
            context.HandoffAt = mode == ConversationMode.Human ? DateTime.UtcNow : null;
            context.LastUpdated = DateTime.UtcNow;
            context.StateVersion = Guid.NewGuid().ToString("N");
            transaction.Update(conversationRef, new Dictionary<string, object> { ["mode"] = mode.ToString(), ["handoffReason"] = reason.ToString() });
            transaction.Set(stateRef, new Dictionary<string, object>
            {
                ["phone"] = phone, ["contextJson"] = System.Text.Json.JsonSerializer.Serialize(context), ["updatedAt"] = context.LastUpdated
            });
        }, cancellationToken: cancellationToken);
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

    public async Task<(int Conversations, int? Handoffs)> CountForPeriodAsync(Guid workspaceId, DateTime start, DateTime end, CancellationToken ct)
    {
        var period = GetCollection(workspaceId).WhereGreaterThanOrEqualTo("startedAt", start).WhereLessThan("startedAt", end);
        var total = await period.Count().GetSnapshotAsync(ct);
        return (checked((int)total.Count!.Value), null);
    }

    public async Task<IEnumerable<ConversationRecord>> GetRecentConversationsAsync(Guid workspaceId, int limit, CancellationToken cancellationToken, DateTime? after = null, string? afterId = null)
    {
        var collection = GetCollection(workspaceId);
        var query = after.HasValue
            ? collection.WhereGreaterThanOrEqualTo("lastMessageAt", after.Value).OrderBy("lastMessageAt").OrderBy(FieldPath.DocumentId)
            : collection.OrderByDescending("lastMessageAt");
        if (after.HasValue && afterId != null) query = query.StartAfter(after.Value, collection.Document(afterId));
        query = query.Limit(limit);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToConversation);
    }

    public async Task<IEnumerable<MessageRecord>> GetMessagesAsync(Guid workspaceId, string conversationId, int limit, CancellationToken cancellationToken, DateTime? after = null, string? afterId = null)
    {
        var collection = GetCollection(workspaceId).Document(conversationId).Collection("messages");
        var query = after.HasValue
            ? collection.WhereGreaterThanOrEqualTo("timestamp", after.Value).OrderBy("timestamp").OrderBy(FieldPath.DocumentId)
            : collection.OrderByDescending("timestamp");
        if (after.HasValue && afterId != null) query = query.StartAfter(after.Value, collection.Document(afterId));
        query = query.Limit(limit);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);

        var messages = snapshot.Documents.Select(MapMessage);
        return after.HasValue ? messages : messages.Reverse();
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
