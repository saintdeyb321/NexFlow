using System.Collections.Frozen;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Engines.AI;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Dialogue.Contracts;

public enum ResolutionStatus { Determined, Ambiguous, Invalid, Insufficient, ProviderUnavailable }
public enum EvidenceSource { ValidatedLiteral, CurrentMenu, LocalResolver, ValidatedLlm }
public enum DialogueFailureDomain { Semantic, Provider, Business, Transport }
public enum SlotResolutionPurpose { Information, Reservation, Order }

public sealed record SlotCandidate<T>(T Value, string RawText, EvidenceSource Source, string? EntityId = null);

// Candidates are evidence, never instructions to change a draft or commit a transaction.
public sealed record SlotResolution<T>
{
    public ResolutionStatus Status { get; }
    public IReadOnlyList<SlotCandidate<T>> Candidates { get; }
    public string Code { get; }
    private SlotResolution(ResolutionStatus status, IEnumerable<SlotCandidate<T>> candidates, string code)
    {
        Status = status; Candidates = Array.AsReadOnly(candidates.ToArray()); Code = code;
    }
    public static SlotResolution<T> Match(T value, string raw, string? id = null, EvidenceSource source = EvidenceSource.LocalResolver)
        => new(ResolutionStatus.Determined, [new(value, raw, source, id)], "Resolved");
    public static SlotResolution<T> Ambiguous(IEnumerable<SlotCandidate<T>> candidates, string code)
        => new(ResolutionStatus.Ambiguous, candidates, code);
    public static SlotResolution<T> Missing(string code = "NotMentioned") => new(ResolutionStatus.Insufficient, [], code);
    public static SlotResolution<T> Invalid(string code) => new(ResolutionStatus.Invalid, [], code);
    public static SlotResolution<T> Unavailable() => new(ResolutionStatus.ProviderUnavailable, [], "ProviderUnavailable");
    public static SlotResolution<T> Incomplete(T candidate, string raw, string code)
        => new(ResolutionStatus.Insufficient, [new(candidate, raw, EvidenceSource.LocalResolver)], code);
    public bool TryGetValue(out T value)
    {
        value = Status == ResolutionStatus.Determined ? Candidates[0].Value : default!;
        return Status == ResolutionStatus.Determined;
    }
}

public sealed record BusinessEntityOption(string Id, string Name, IReadOnlyList<string>? Aliases = null, string? LocationId = null);
public sealed record ResolvedBusinessEntity(string Id, string Name, string? LocationId = null);

public enum TimePreferenceKind { Exact, Period, After, Before }
// Bounds describe a preference; they are not a claim of business opening hours or availability.
public sealed record TimePreference(TimePreferenceKind Kind, TimeOnly? ExactTime = null, TimeOnly? From = null, TimeOnly? Until = null);

// Ephemeral snapshot of existing state, not another persisted conversation store.
public sealed record DialogueTurnContext(Guid WorkspaceId, string SourceMessageId, string? CurrentGoal, string? CurrentStep,
    string? StateVersion, string? SelectedLocationId, string? LastQuestion, DateTimeOffset LocalBusinessNow,
    string TimeZoneId, IReadOnlySet<string> EnabledModules)
{
    public IReadOnlyList<string> LocationHints { get; init; } = [];
    public IReadOnlyList<string> ServiceHints { get; init; } = [];
    public string? DraftDate { get; init; }
    public static DialogueTurnContext Capture(Guid workspace, string source, ConversationContextDto state,
        DateTimeOffset localNow, TimeZoneInfo zone, IEnumerable<string> modules) => new(workspace, source,
            state.CurrentGoal, state.CurrentStep, state.StateVersion, state.SelectedLocationId, state.LastQuestion,
            localNow, zone.Id, modules.ToFrozenSet(StringComparer.OrdinalIgnoreCase)) { DraftDate = state.TargetDate };
}

public sealed record TurnUnderstanding(DialogueTurnContext Context, SlotResolution<string> Name,
    SlotResolution<ResolvedBusinessEntity> Location, SlotResolution<ResolvedBusinessEntity> Service,
    SlotResolution<ResolvedBusinessEntity> Product, SlotResolution<DateOnly> Date, SlotResolution<TimePreference> Time)
{
    // Phase 1 deliberately does not infer actions, confirm operations or merge state.
    public ConversationIntent? Intent { get; init; }
    public IReadOnlyList<BusinessEntityOption> AuthorizedLocations { get; init; } = [];
    public IReadOnlyList<BusinessEntityOption> AuthorizedServices { get; init; } = [];
    public IReadOnlyList<BusinessEntityOption> AuthorizedProducts { get; init; } = [];
}

public sealed record DialogueFailure(DialogueFailureDomain Domain, string Code)
{
    // Classification only: no retries, counters or delivery transitions are introduced here.
    public static DialogueFailure? FromProvider(AiFailureKind failure) => failure == AiFailureKind.None ? null
        : new(DialogueFailureDomain.Provider, failure.ToString());
    public static DialogueFailure? FromDelivery(MessageStatus status) => status is MessageStatus.Failed or MessageStatus.UnknownDelivery
        ? new(DialogueFailureDomain.Transport, status.ToString()) : null;
    public static DialogueFailure? FromResolution<T>(SlotResolution<T> slot) => slot.Code == "ModuleDenied"
        ? new(DialogueFailureDomain.Business, "ModuleDenied")
        : slot.Status == ResolutionStatus.ProviderUnavailable ? new(DialogueFailureDomain.Provider, "Unavailable")
        : slot.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid ? new(DialogueFailureDomain.Semantic, slot.Status.ToString()) : null;
}
public enum VerifiedFactSource { BusinessRepository, TransactionEngine, ValidatedConversation, AuthorizedWorkflow }
public sealed record VerifiedFact(string Key, string Value, VerifiedFactSource Source, string? EntityId = null, bool Sensitive = false);
