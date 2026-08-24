using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Conversations;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreConsumerIdentityRepository : IConsumerIdentityRepository
{
    private readonly FirestoreDb _db;

    public FirestoreConsumerIdentityRepository(FirestoreDb db)
    {
        _db = db;
    }

    private CollectionReference GetCollection(Guid workspaceId) =>
        _db.Collection("workspaces").Document(workspaceId.ToString()).Collection("consumers");

    public async Task<ConsumerIdentityRecord?> GetConsumerAsync(Guid workspaceId, string phone, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(phone);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);

        if (!snapshot.Exists) return null;

        return new ConsumerIdentityRecord
        {
            Phone = snapshot.Id,
            DisplayName = snapshot.TryGetValue("displayName", out string name) ? name : null,
            FirstSeenAt = snapshot.GetValue<Timestamp>("firstSeenAt").ToDateTime(),
            LastInteractionAt = snapshot.GetValue<Timestamp>("lastInteractionAt").ToDateTime()
        };
    }

    public async Task UpsertConsumerAsync(Guid workspaceId, ConsumerIdentityRecord consumer, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(consumer.Phone);

        // 🔥 CORRECCIÓN SPRINT 6: Caducidad a 90 días desde la última interacción
        var expiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(90), DateTimeKind.Utc);

        var data = new Dictionary<string, object>
        {
            { "phone", consumer.Phone },
            { "lastInteractionAt", DateTime.SpecifyKind(consumer.LastInteractionAt, DateTimeKind.Utc) },
            { "expiresAt", expiresAt } // Inyectamos el TTL rotativo
        };

        // Solo actualizamos el nombre si no es nulo
        if (!string.IsNullOrEmpty(consumer.DisplayName))
            data["displayName"] = consumer.DisplayName;

        // Utilizamos MergeAll para que, si el documento ya existe, no se sobrescriba el FirstSeenAt
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists)
        {
            data["firstSeenAt"] = DateTime.SpecifyKind(consumer.FirstSeenAt, DateTimeKind.Utc);
        }

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }
}