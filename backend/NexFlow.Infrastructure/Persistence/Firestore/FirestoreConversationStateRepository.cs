using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using System.Text.Json;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreConversationStateRepository : IConversationStateRepository
{
    private readonly FirestoreDb _db;

    public FirestoreConversationStateRepository(FirestoreDb db) => _db = db;

    private DocumentReference GetDocRef(Guid workspaceId, string phone) =>
        _db.Collection("workspaces").Document(workspaceId.ToString()).Collection("conversationStates").Document(phone);

    public async Task<ConversationContextDto?> GetStateAsync(Guid workspaceId, string phone, CancellationToken cancellationToken)
    {
        var snapshot = await GetDocRef(workspaceId, phone).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists) return null;

        var json = snapshot.GetValue<string>("contextJson");
        return JsonSerializer.Deserialize<ConversationContextDto>(json)
            ?? throw new InvalidOperationException("Persisted conversation state is invalid.");
    }

    public async Task UpsertStateAsync(Guid workspaceId, string phone, ConversationContextDto state, CancellationToken cancellationToken)
    {
        var docRef = GetDocRef(workspaceId, phone);
        var expectedVersion = state.StateVersion;
        var nextVersion = Guid.NewGuid().ToString("N");
        var persisted = JsonSerializer.Deserialize<ConversationContextDto>(JsonSerializer.Serialize(state))!;
        persisted.StateVersion = nextVersion;
        await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(docRef, cancellationToken);
            var current = snapshot.Exists
                ? JsonSerializer.Deserialize<ConversationContextDto>(snapshot.GetValue<string>("contextJson"))
                    ?? throw new InvalidOperationException("Persisted conversation state is invalid.")
                : null;
            if (current?.StateVersion != expectedVersion)
                throw new InvalidOperationException("Conversation state changed concurrently; reload before saving.");

            transaction.Set(docRef, new Dictionary<string, object>
            {
                ["phone"] = phone,
                ["contextJson"] = JsonSerializer.Serialize(persisted),
                ["updatedAt"] = persisted.LastUpdated
            });
        }, cancellationToken: cancellationToken);
        state.StateVersion = nextVersion;
    }

    public async Task DeleteStateAsync(Guid workspaceId, string phone, CancellationToken cancellationToken)
    {
        await GetDocRef(workspaceId, phone).DeleteAsync(cancellationToken: cancellationToken);
    }
}