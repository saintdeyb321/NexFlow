using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreConversationRepository : IConversationRepository
{
    private readonly FirestoreDb _db;

    public FirestoreConversationRepository(FirestoreDb db)
    {
        _db = db;
    }

    private CollectionReference GetCollection(Guid workspaceId) =>
        _db.Collection("workspaces").Document(workspaceId.ToString()).Collection("conversations");

    public async Task<ConversationRecord?> GetActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken)
    {
        var query = GetCollection(workspaceId)
            .WhereEqualTo("consumerPhone", consumerPhone)
            .WhereEqualTo("status", "open")
            .OrderByDescending("startedAt")
            .Limit(1);

        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        var doc = snapshot.Documents.FirstOrDefault();

        if (doc == null) return null;

        return MapToConversation(doc);
    }

    public async Task<ConversationRecord> GetOrCreateActiveConversationAsync(Guid workspaceId, string consumerPhone, CancellationToken cancellationToken)
    {
        var collection = GetCollection(workspaceId);

        return await _db.RunTransactionAsync(async transaction =>
        {
            var query = collection
                .WhereEqualTo("consumerPhone", consumerPhone)
                .WhereEqualTo("status", "open")
                .OrderByDescending("startedAt")
                .Limit(1);

            var snapshot = await transaction.GetSnapshotAsync(query, cancellationToken);
            var doc = snapshot.Documents.FirstOrDefault();

            if (doc != null)
            {
                return MapToConversation(doc);
            }

            var newConv = new ConversationRecord
            {
                Id = Guid.NewGuid().ToString(),
                ConsumerPhone = consumerPhone,
                Channel = "whatsapp",
                Mode = ConversationMode.Automatic,
                Status = "open",
                StartedAt = DateTime.UtcNow,
                LastMessageAt = DateTime.UtcNow
            };

            var docRef = collection.Document(newConv.Id);
            var expiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(90), DateTimeKind.Utc);
            var data = new Dictionary<string, object>
            {
                { "id", newConv.Id },
                { "consumerPhone", newConv.ConsumerPhone },
                { "channel", newConv.Channel },
                { "mode", newConv.Mode.ToString() },
                { "status", newConv.Status },
                { "startedAt", DateTime.SpecifyKind(newConv.StartedAt, DateTimeKind.Utc) },
                { "lastMessageAt", DateTime.SpecifyKind(newConv.LastMessageAt, DateTimeKind.Utc) },
                { "expiresAt", expiresAt }
            };

            transaction.Set(docRef, data);
            return newConv;
        }, cancellationToken: cancellationToken);
    }

    public async Task CreateConversationAsync(Guid workspaceId, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(conversation.Id);
        var expiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(90), DateTimeKind.Utc);

        var data = new Dictionary<string, object>
        {
            { "id", conversation.Id },
            { "consumerPhone", conversation.ConsumerPhone },
            { "channel", conversation.Channel },
            { "mode", conversation.Mode.ToString() },
            { "status", conversation.Status },
            { "startedAt", DateTime.SpecifyKind(conversation.StartedAt, DateTimeKind.Utc) },
            { "lastMessageAt", DateTime.SpecifyKind(conversation.LastMessageAt, DateTimeKind.Utc) },
            { "expiresAt", expiresAt }
        };

        await docRef.SetAsync(data, cancellationToken: cancellationToken);
    }

    public async Task UpdateConversationModeAsync(Guid workspaceId, string conversationId, ConversationMode mode, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(conversationId);
        var updates = new Dictionary<string, object> { { "mode", mode.ToString() } };
        await docRef.UpdateAsync(updates, cancellationToken: cancellationToken);
    }

    public async Task AddMessageAsync(Guid workspaceId, string conversationId, MessageRecord message, CancellationToken cancellationToken)
    {
        var messageRef = GetCollection(workspaceId).Document(conversationId).Collection("messages").Document(message.Id);

        // 🔥 Auditoría (Fase 3): El TTL está configurado correctamente en el mensaje hijo. 
        // Firestore borrará este documento independientemente de su padre.
        var expiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(90), DateTimeKind.Utc);

        var data = new Dictionary<string, object>
        {
            { "id", message.Id },
            { "direction", message.Direction },
            { "sender", message.Sender.ToString() },
            { "content", message.Content },
            { "timestamp", DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc) },
            { "expiresAt", expiresAt } // Clave para el TTL
        };

        if (!string.IsNullOrEmpty(message.ExternalMessageId))
            data["externalMessageId"] = message.ExternalMessageId;

        await messageRef.SetAsync(data, cancellationToken: cancellationToken);

        var convRef = GetCollection(workspaceId).Document(conversationId);
        await convRef.UpdateAsync(new Dictionary<string, object>
        {
            { "lastMessageAt", DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc) },
            { "expiresAt", expiresAt }
        }, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<ConversationRecord>> GetRecentConversationsAsync(Guid workspaceId, int limit, CancellationToken cancellationToken)
    {
        var query = GetCollection(workspaceId)
            .OrderByDescending("lastMessageAt")
            .Limit(limit);

        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToConversation);
    }

    public async Task<IEnumerable<MessageRecord>> GetMessagesAsync(Guid workspaceId, string conversationId, int limit, CancellationToken cancellationToken)
    {
        try
        {
            var query = GetCollection(workspaceId).Document(conversationId).Collection("messages")
                .OrderByDescending("timestamp")
                .Limit(limit);

            var snapshot = await query.GetSnapshotAsync(cancellationToken);

            return snapshot.Documents.Select(d => new MessageRecord
            {
                Id = d.Id,
                Direction = d.GetValue<string>("direction"),
                Sender = Enum.Parse<SenderType>(d.GetValue<string>("sender")),
                Content = d.GetValue<string>("content"),
                ExternalMessageId = d.TryGetValue("externalMessageId", out string extId) ? extId : null,
                Timestamp = d.GetValue<Timestamp>("timestamp").ToDateTime()
            }).Reverse();
        }
        catch (OperationCanceledException)
        {
            // El frontend (ej. un useEffect de React) abortó la petición antes de tiempo.
            // Devolvemos una lista vacía silenciosamente sin colapsar el backend.
            return Enumerable.Empty<MessageRecord>();
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled)
        {
            // Captura específica para cancelaciones nativas de gRPC/Firestore
            return Enumerable.Empty<MessageRecord>();
        }
    }

    private static ConversationRecord MapToConversation(DocumentSnapshot doc)
    {
        return new ConversationRecord
        {
            Id = doc.Id,
            ConsumerPhone = doc.GetValue<string>("consumerPhone"),
            Channel = doc.GetValue<string>("channel"),
            Mode = Enum.Parse<ConversationMode>(doc.GetValue<string>("mode")),
            Status = doc.GetValue<string>("status"),
            StartedAt = doc.GetValue<Timestamp>("startedAt").ToDateTime(),
            LastMessageAt = doc.GetValue<Timestamp>("lastMessageAt").ToDateTime()
        };
    }

    public async Task<ConversationRecord?> GetConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(conversationId);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);

        if (!snapshot.Exists) return null;

        return MapToConversation(snapshot);
    }
    public async Task DeleteConversationAsync(Guid workspaceId, string conversationId, CancellationToken cancellationToken)
    {
        var convRef = GetCollection(workspaceId).Document(conversationId);

        // 1. Borramos la subcolección de mensajes primero
        var messagesSnapshot = await convRef.Collection("messages").GetSnapshotAsync(cancellationToken);
        foreach (var messageDoc in messagesSnapshot.Documents)
        {
            await messageDoc.Reference.DeleteAsync(Precondition.None, cancellationToken);
        }

        // 2. Borramos el documento de la conversación padre
        await convRef.DeleteAsync(Precondition.None, cancellationToken);
    }
}