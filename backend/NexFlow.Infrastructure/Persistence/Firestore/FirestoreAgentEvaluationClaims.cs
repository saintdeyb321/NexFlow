using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;
using Grpc.Core;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Agent;

namespace NexFlow.Infrastructure.Persistence.Firestore;

// Shares the existing workspace AI-budget namespace. No conversation fields or
// generated text are stored. The marker is never released on timeout/uncertainty.
public sealed class FirestoreAgentEvaluationClaims(FirestoreDb db, IClock clock) : IAgentEvaluationClaims
{
    public async Task<AgentEvaluationClaim> TryBeginAsync(AgentTurnRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.WorkspaceId == Guid.Empty || string.IsNullOrWhiteSpace(request.SourceMessageId) || request.SourceMessageId.Length > 200
            || string.IsNullOrWhiteSpace(request.ConversationId) || request.ConversationId.Length > 200
            || string.IsNullOrWhiteSpace(request.ClientKey) || request.ClientKey.Length > 200
            || string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 2000 || clock.UtcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid evaluation identity.");
        var reference = db.Collection("workspaces").Document(request.WorkspaceId.ToString()).Collection("conversationStates")
            .Document("ai-budget").Collection("shadowEvaluations").Document(Hash(request.SourceMessageId));
        var fingerprint = Hash(JsonSerializer.Serialize(new { request.WorkspaceId, request.ConversationId, request.ClientKey, request.Text }));
        try
        {
            // Create uses exists=false on the server: concurrent replicas cannot
            // both win. There is no read-then-overwrite or in-memory lease.
            await reference.CreateAsync(new Dictionary<string, object> {
                ["version"] = 1L, ["requestFingerprint"] = fingerprint, ["startedAt"] = clock.UtcNow
            }, ct);
            return AgentEvaluationClaim.Started;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            var stored = await reference.GetSnapshotAsync(ct);
            return stored.Exists && stored.TryGetValue<long>("version", out var version) && version == 1
                && stored.TryGetValue<string>("requestFingerprint", out var known) && known == fingerprint
                ? AgentEvaluationClaim.Duplicate : AgentEvaluationClaim.Conflict;
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
