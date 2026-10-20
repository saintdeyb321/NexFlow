using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

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
                throw new NexFlow.Domain.Exceptions.ConcurrencyException("Conversation state changed concurrently; reload before saving.");

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

    public async Task<bool> TryConsumeAiBudgetAsync(Guid workspaceId, string phone, string sourceMessageId, DateTime now,
        int clientLimit, int workspaceLimit, CancellationToken ct)
    {
        if (workspaceId == Guid.Empty || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(sourceMessageId)
            || clientLimit < 1 || workspaceLimit < 1) throw new ArgumentException("Invalid assistant budget identity.");
        var reference = GetDocRef(workspaceId, "ai-budget");
        var client = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phone)));
        var source = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{phone}:{sourceMessageId}")));
        var window = now.Ticks / TimeSpan.TicksPerMinute;
        return await _db.RunTransactionAsync(async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(reference, ct);
            var budget = snapshot.Exists ? JsonSerializer.Deserialize<AiBudget>(snapshot.GetValue<string>("budgetJson"))! : new AiBudget();
            // An older in-flight request must never roll back a newer quota window.
            if (budget.Window > window) return false;
            if (budget.Window < window) budget = new() { Window = window };
            if (budget.Sources.Contains(source)) return true;
            var count = budget.Clients.GetValueOrDefault(client);
            if (budget.Sources.Count >= workspaceLimit || count >= clientLimit) return false;
            budget.Clients[client] = count + 1; budget.Sources.Add(source);
            tx.Set(reference, new Dictionary<string, object> { ["budgetJson"] = JsonSerializer.Serialize(budget) });
            return true;
        }, cancellationToken: ct);
    }
    private sealed class AiBudget
    {
        public long Window { get; set; }
        public Dictionary<string, int> Clients { get; set; } = new();
        public HashSet<string> Sources { get; set; } = new();
    }
}
