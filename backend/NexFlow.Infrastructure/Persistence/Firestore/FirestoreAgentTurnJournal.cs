using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Infrastructure.Persistence.Firestore;

// Evaluation results, never an outbox. Payload is server-realized public text;
// no input text/phone, free model narrative or duplicate business draft is stored.
public sealed class FirestoreAgentTurnJournal(FirestoreDb db, IClock clock) : IAgentTurnJournal
{
    private sealed record Stored(int Version, string Fingerprint, string Fence, AgentJournalStatus Status,
        string? OriginVersion, string OriginFingerprint, string? AppliedVersion, DateTime Deadline,
        DateTime RetainUntil, AgentTurnResult? Result, bool PayloadCleared = false);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Identity(AgentTurnRequest r) => Hash(JsonSerializer.Serialize(new { r.WorkspaceId, r.ConversationId, r.SourceMessageId, r.ClientKey, r.Text }));
    private DocumentReference Reference(AgentTurnRequest r) => db.Collection("workspaces").Document(r.WorkspaceId.ToString())
        .Collection("agentTurnResults").Document(Hash(JsonSerializer.Serialize(new { r.ConversationId, r.SourceMessageId })));
    private DocumentReference Context(AgentTurnRequest r) => db.Collection("workspaces").Document(r.WorkspaceId.ToString())
        .Collection("conversationStates").Document(r.ClientKey);
    private static Stored Load(DocumentSnapshot doc) => JsonSerializer.Deserialize<Stored>(doc.GetValue<string>("resultJson"))
        is { Version: 2 } value && Enum.IsDefined(value.Status) && value.Status != AgentJournalStatus.Conflict
            ? value : throw new InvalidOperationException("Invalid agent result record.");
    private static Dictionary<string, object> Fields(Stored value) => new() { ["resultJson"] = JsonSerializer.Serialize(value) };
    private static AgentJournalEntry View(Stored value, bool owned = false) => new(value.Status,
        owned ? new("", value.Fingerprint, value.Fence, value.OriginVersion, value.OriginFingerprint, value.Deadline) : null,
        value.Result, value.AppliedVersion ?? value.OriginVersion);

    // Trusted maintenance entry, no worker/API or automatic real-store invocation.
    // Bounded scan also covers older receipts without a newly indexed TTL field.
    public async Task<(int Cleared, string? NextCursor)> CompactBatchAsync(Guid workspace, string? cursor, int limit, CancellationToken ct)
    {
        if (workspace == Guid.Empty || AgentExperimentScope.Current?.Workspace != workspace || limit is < 1 or > 100
            || cursor != null && (cursor.Length != 64 || !cursor.All(char.IsAsciiHexDigit)))
            throw new ArgumentException("Only scoped synthetic journal maintenance is allowed.");
        Query query = db.Collection("workspaces").Document(workspace.ToString()).Collection("agentTurnResults").OrderBy(FieldPath.DocumentId);
        if (cursor != null) query = query.StartAfter(cursor);
        var page = await query.Limit(limit).GetSnapshotAsync(ct); var cleared = 0;
        foreach (var document in page.Documents) {
            var changed = await db.RunTransactionAsync(async tx => {
                var current = await tx.GetSnapshotAsync(document.Reference, ct); if (!current.Exists) return false;
                var value = Load(current);
                if (value.PayloadCleared || value.Status == AgentJournalStatus.Started && clock.UtcNow < value.Deadline
                    || value.Status != AgentJournalStatus.Started && clock.UtcNow < value.RetainUntil) return false;
                // Preserve identity/fence and the claim forever; no replay re-admission.
                tx.Set(document.Reference, Fields(value with { Status = AgentJournalStatus.Failed, PayloadCleared = true,
                    Result = AgentSafeReplies.Failure(value.Status == AgentJournalStatus.Started ? "Abandoned" : "ResultExpired") }));
                return true;
            }, cancellationToken: ct);
            if (changed) cleared++;
        }
        return (cleared, page.Documents.Count == limit ? page.Documents.Last().Id : null);
    }

    public Task<AgentJournalEntry> ObserveAsync(AgentTurnRequest request, CancellationToken ct)
        => db.RunTransactionAsync(async tx => {
            var reference = Reference(request); var doc = await tx.GetSnapshotAsync(reference, ct);
            if (!doc.Exists) return new AgentJournalEntry(AgentJournalStatus.Failed);
            var value = Load(doc);
            if (value.Fingerprint != Identity(request)) return new AgentJournalEntry(AgentJournalStatus.Conflict);
            if (value.Status == AgentJournalStatus.Started && clock.UtcNow >= value.Deadline
                || value.Status == AgentJournalStatus.Prepared && clock.UtcNow >= value.RetainUntil) {
                value = value with { Status = AgentJournalStatus.Failed,
                    Result = AgentSafeReplies.Failure(value.Status == AgentJournalStatus.Started ? "Abandoned" : "ResultExpired") };
                tx.Set(reference, Fields(value));
            }
            return View(value);
        }, cancellationToken: ct);

    public Task<AgentJournalEntry> BeginAsync(AgentTurnRequest request, ConversationContextDto origin, AgentDialogueOptions options, CancellationToken ct)
    {
        if (request.WorkspaceId == Guid.Empty || string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.SourceMessageId)
            || request.ConversationId.Length > 200 || request.SourceMessageId.Length > 200 || string.IsNullOrWhiteSpace(request.ClientKey)
            || request.ClientKey.Length > 200 || request.Text.Length > 2000 || options.ExecutionSeconds is < 1 or > 60
            || options.ResultRetentionHours is < 1 or > 168 || clock.UtcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid bounded agent journal request.");
        var reference = Reference(request); var identity = Identity(request); var fence = Guid.NewGuid().ToString("N");
        return db.RunTransactionAsync(async tx => {
            var doc = await tx.GetSnapshotAsync(reference, ct);
            if (doc.Exists) {
                var value = Load(doc);
                if (value.Fingerprint != identity) return new AgentJournalEntry(AgentJournalStatus.Conflict);
                if ((value.Status == AgentJournalStatus.Started && clock.UtcNow >= value.Deadline)
                    || (value.Status == AgentJournalStatus.Prepared && clock.UtcNow >= value.RetainUntil)) {
                    value = value with { Status = AgentJournalStatus.Failed,
                        Result = AgentSafeReplies.Failure(value.Status == AgentJournalStatus.Started ? "Abandoned" : "ResultExpired") };
                    tx.Set(reference, Fields(value));
                }
                return View(value);
            }
            var contextDoc = await tx.GetSnapshotAsync(Context(request), ct);
            var current = contextDoc.Exists ? JsonSerializer.Deserialize<ConversationContextDto>(contextDoc.GetValue<string>("contextJson"))! : new();
            if (current.Mode == "Human" || current.StateVersion != origin.StateVersion
                || ConversationQuestion.Fingerprint(current) != ConversationQuestion.Fingerprint(origin))
                return new AgentJournalEntry(AgentJournalStatus.Failed, Result: AgentSafeReplies.Failure("StaleContext", AgentSafeReplies.Stale));
            var started = new Stored(2, identity, fence, AgentJournalStatus.Started, origin.StateVersion,
                ConversationQuestion.Fingerprint(origin), null, clock.UtcNow.AddSeconds(options.ExecutionSeconds),
                clock.UtcNow.AddHours(options.ResultRetentionHours), null);
            tx.Set(reference, Fields(started));
            var entry = View(started, true);
            return entry with { Lease = entry.Lease! with { Key = reference.Path } };
        }, cancellationToken: ct);
    }

    public Task<AgentJournalEntry> CompleteAsync(AgentTurnRequest request, AgentTurnLease lease, ConversationContextDto? next,
        AgentTurnResult safeResult, CancellationToken ct)
    {
        var reference = Reference(request);
        if (lease.Key != reference.Path || lease.Fingerprint != Identity(request) || !safeResult.Publishable
            || safeResult.VerifiedText!.Length > 5000 || safeResult.Proposal?.CandidateResponse.Length > 0
            || safeResult.Proposal?.Evidence.Length > 0)
            throw new ArgumentException("Only server-realized results may be journaled.");
        return db.RunTransactionAsync(async tx => {
            var doc = await tx.GetSnapshotAsync(reference, ct);
            if (!doc.Exists) throw new InvalidOperationException("Missing agent journal claim.");
            var value = Load(doc);
            if (value.Fingerprint != Identity(request)) return new AgentJournalEntry(AgentJournalStatus.Conflict);
            if (value.Status != AgentJournalStatus.Started || value.Fence != lease.Fence) return View(value);
            if (value.OriginVersion != lease.ContextVersion || value.OriginFingerprint != lease.ContextFingerprint || value.Deadline != lease.Deadline)
                throw new InvalidOperationException("Agent lease does not match durable generation.");
            var contextReference = Context(request); var contextDoc = await tx.GetSnapshotAsync(contextReference, ct);
            var current = contextDoc.Exists ? JsonSerializer.Deserialize<ConversationContextDto>(contextDoc.GetValue<string>("contextJson"))! : new();
            if (clock.UtcNow >= value.Deadline || current.Mode == "Human" || current.StateVersion != lease.ContextVersion
                || ConversationQuestion.Fingerprint(current) != lease.ContextFingerprint) {
                var failed = value with { Status = AgentJournalStatus.Failed,
                    AppliedVersion = current.StateVersion, Result = AgentSafeReplies.Failure("StaleContext", AgentSafeReplies.Stale) };
                tx.Set(reference, Fields(failed)); return View(failed);
            }
            var appliedVersion = current.StateVersion;
            if (next != null) {
                var detached = JsonSerializer.Deserialize<ConversationContextDto>(JsonSerializer.Serialize(next))!;
                detached.StateVersion = appliedVersion = Guid.NewGuid().ToString("N"); detached.LastUpdated = clock.UtcNow;
                tx.Set(contextReference, new Dictionary<string, object> { ["phone"] = request.ClientKey,
                    ["contextJson"] = JsonSerializer.Serialize(detached), ["updatedAt"] = detached.LastUpdated });
            }
            var prepared = value with { Status = safeResult.Accepted ? AgentJournalStatus.Prepared : AgentJournalStatus.Failed,
                AppliedVersion = appliedVersion, Result = safeResult };
            tx.Set(reference, Fields(prepared)); return View(prepared);
        }, cancellationToken: ct);
    }
}
