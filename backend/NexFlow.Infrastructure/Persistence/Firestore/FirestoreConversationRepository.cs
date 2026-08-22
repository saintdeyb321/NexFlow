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

    public async Task CreateConversationAsync(Guid workspaceId, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(conversation.Id);

        // Calculamos la fecha de expiración (90 días a partir de hoy)
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
            { "expiresAt", expiresAt } // <-- Inyectamos la expiración
        };

        await docRef.SetAsync(data, cancellationToken: cancellationToken);
    }

    public async Task UpdateConversationModeAsync(Guid workspaceId, string conversationId, ConversationMode mode, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(conversationId);

        var updates = new Dictionary<string, object>
        {
            { "mode", mode.ToString() }
        };

        await docRef.UpdateAsync(updates, cancellationToken: cancellationToken);
    }

    public async Task AddMessageAsync(Guid workspaceId, string conversationId, MessageRecord message, CancellationToken cancellationToken)
    {
        var messageRef = GetCollection(workspaceId).Document(conversationId).Collection("messages").Document(message.Id);
        var expiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(90), DateTimeKind.Utc);

        var data = new Dictionary<string, object>
        {
            { "id", message.Id },
            { "direction", message.Direction },
            { "sender", message.Sender.ToString() },
            { "content", message.Content },
            { "timestamp", DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc) },
            { "expiresAt", expiresAt } // <-- Mensaje caducará automáticamente
        };

        if (!string.IsNullOrEmpty(message.ExternalMessageId))
            data["externalMessageId"] = message.ExternalMessageId;

        await messageRef.SetAsync(data, cancellationToken: cancellationToken);

        // Actualizamos el LastMessageAt y REFRESCAMOS el ExpiresAt de la conversación padre para que no muera mientras esté activa
        var convRef = GetCollection(workspaceId).Document(conversationId);
        await convRef.UpdateAsync(new Dictionary<string, object>
        {
            { "lastMessageAt", DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc) },
            { "expiresAt", expiresAt } // Extendemos su vida útil
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
        var query = GetCollection(workspaceId).Document(conversationId).Collection("messages")
            .OrderByDescending("timestamp")
            .Limit(limit);

        var snapshot = await query.GetSnapshotAsync(cancellationToken);

        // Invertimos en memoria para que el frontend los reciba en orden cronológico (más viejo al más nuevo)
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
}