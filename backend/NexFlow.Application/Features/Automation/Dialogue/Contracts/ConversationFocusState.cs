using NexFlow.Application.Abstractions.Cache;

namespace NexFlow.Application.Features.Automation.Dialogue.Contracts;

public enum OperationKind { Order, Reservation }
public enum DraftDiscardScope { Active, Suspended, Both }
public sealed record DraftDiscardDecision(DraftDiscardScope? Scope, string SourceMessageId, DateTime ExpiresAt,
    string Fingerprint, string? QuestionId = null);

public sealed record OperationIdentity(Guid Id, OperationKind Kind, long ActivationRevision, DateTime ExpiresAt);

// One active engine projection plus at most one detached suspended draft. No
// StateVersion, old question/consent, menus, prices or commit receipts in snapshots.
public sealed class ConversationFocusState
{
    public int Version { get; set; } = 1;
    public Guid WorkspaceId { get; set; }
    public string ConversationId { get; set; } = "";
    public OperationIdentity Active { get; set; } = null!;
    public SuspendedConversationDraft? Suspended { get; set; }
    public string? LastTransitionSource { get; set; }
    public bool LastTransitionResumed { get; set; }
    public DraftDiscardDecision? PendingDiscard { get; set; }
}

public sealed record SuspendedConversationDraft(OperationIdentity Identity, DateTime SuspendedAt,
    string? LocationId, IReadOnlyList<OrderDraftItem> Items, string? ServiceId = null,
    string? Date = null, string? Time = null, string? CustomerName = null, string? PendingCustomerName = null,
    IReadOnlyList<string>? PendingServiceIds = null, IReadOnlyList<string>? PendingSlots = null, TimePreference? PreferredTime = null);

// Backend-created from one evidence-backed semantic act; never deserialized from provider JSON.
public sealed record FocusChangeProposal(Guid WorkspaceId, string SourceMessageId, string? ExpectedStateVersion,
    OperationKind Target, bool ResumeOnly);
