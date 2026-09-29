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
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<ConversationContextDto>(json);
    }

    public async Task UpsertStateAsync(Guid workspaceId, string phone, ConversationContextDto state, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object>
        {
            { "phone", phone },
            { "contextJson", JsonSerializer.Serialize(state) },
            { "updatedAt", DateTime.UtcNow }
        };
        await GetDocRef(workspaceId, phone).SetAsync(data, cancellationToken: cancellationToken);
    }

    public async Task DeleteStateAsync(Guid workspaceId, string phone, CancellationToken cancellationToken)
    {
        await GetDocRef(workspaceId, phone).DeleteAsync(cancellationToken: cancellationToken);
    }
}