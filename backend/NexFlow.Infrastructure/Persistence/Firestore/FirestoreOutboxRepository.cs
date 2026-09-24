using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities.System;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreOutboxRepository : IOutboxRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreOutboxRepository(FirestoreDb firestoreDb)
    {
        _firestoreDb = firestoreDb;
    }

    // Usamos una colección raíz para el Outbox para que el Worker pueda leer todos los workspaces a la vez
    private CollectionReference GetCollection() => _firestoreDb.Collection("outbox_messages");

    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var docRef = GetCollection().Document(message.Id);

        var data = new Dictionary<string, object>
        {
            { "Id", message.Id },
            { "WorkspaceId", message.WorkspaceId.ToString() },
            { "EventType", message.EventType },
            { "PayloadJson", message.PayloadJson },
            { "Status", message.Status.ToString() },
            { "CreatedAt", message.CreatedAt.ToUniversalTime() },
            { "RetryCount", message.RetryCount }
        };

        if (message.ProcessedAt.HasValue) data["ProcessedAt"] = message.ProcessedAt.Value.ToUniversalTime();
        if (!string.IsNullOrEmpty(message.Error)) data["Error"] = message.Error;

        await docRef.SetAsync(data, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<OutboxMessage>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken)
    {
        var snapshot = await GetCollection()
            .WhereEqualTo("Status", OutboxStatus.Pending.ToString())
            .OrderBy("CreatedAt")
            .Limit(batchSize)
            .GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Select(doc => new OutboxMessage
        {
            Id = doc.GetValue<string>("Id"),
            WorkspaceId = Guid.Parse(doc.GetValue<string>("WorkspaceId")),
            EventType = doc.GetValue<string>("EventType"),
            PayloadJson = doc.GetValue<string>("PayloadJson"),
            Status = Enum.Parse<OutboxStatus>(doc.GetValue<string>("Status")),
            CreatedAt = doc.GetValue<DateTime>("CreatedAt"),
            RetryCount = doc.GetValue<int>("RetryCount")
        }).ToList();
    }

    public async Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var docRef = GetCollection().Document(message.Id);

        var updates = new Dictionary<string, object>
        {
            { "Status", message.Status.ToString() },
            { "RetryCount", message.RetryCount }
        };

        if (message.ProcessedAt.HasValue) updates["ProcessedAt"] = message.ProcessedAt.Value.ToUniversalTime();
        if (!string.IsNullOrEmpty(message.Error)) updates["Error"] = message.Error;

        await docRef.UpdateAsync(updates, cancellationToken: cancellationToken);
    }
}