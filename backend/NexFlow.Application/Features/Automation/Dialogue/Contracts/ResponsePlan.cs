using System.Collections.ObjectModel;
using NexFlow.Domain.Enums;
using NexFlow.Application.Abstractions.Cache;

namespace NexFlow.Application.Features.Automation.Dialogue.Contracts;

public enum OperationStatus { Informational, Collecting, AwaitingConfirmation, Succeeded, NotAvailable, Cancelled, Denied, Human, MediaPending }

// A detached, immutable value for one authorized result. Never persisted as another history.
public sealed class ResponsePlan
{
    public Guid WorkspaceId { get; }
    public string SourceMessageId { get; }
    public string? StateVersion { get; }
    public ConversationIntent Intent { get; }
    public DialogueAct Act { get; }
    public OperationStatus Operation { get; }
    public ReadOnlyCollection<VerifiedFact> Facts { get; }
    public ReadOnlyCollection<ConversationMenuOption> Choices { get; }
    public ReadOnlyCollection<string> NeededData { get; }
    public string? TimeZone { get; }
    public string? Currency { get; }
    public string? NextQuestion { get; }
    public string FallbackText { get; }
    public bool AllowCreativeParaphrase { get; }
    public VerifiedResponseLayout? Realization { get; init; }

    public ResponsePlan(Guid workspaceId, string sourceMessageId, string? stateVersion, ConversationIntent intent,
        DialogueAct act, OperationStatus operation, IEnumerable<VerifiedFact> facts, IEnumerable<ConversationMenuOption> choices,
        IEnumerable<string> neededData, string? timeZone, string? currency, string? nextQuestion, string fallbackText, bool allowCreativeParaphrase = false)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Workspace required.", nameof(workspaceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMessageId); ArgumentException.ThrowIfNullOrWhiteSpace(fallbackText);
        WorkspaceId = workspaceId; SourceMessageId = sourceMessageId; StateVersion = stateVersion;
        Intent = intent; Act = act; Operation = operation;
        Facts = Array.AsReadOnly(facts.ToArray()); Choices = Array.AsReadOnly(choices.Select(c => new ConversationMenuOption(c.Key, c.Label, c.Kind)).ToArray());
        NeededData = Array.AsReadOnly(neededData.ToArray()); TimeZone = timeZone; Currency = currency;
        NextQuestion = nextQuestion; FallbackText = fallbackText; AllowCreativeParaphrase = allowCreativeParaphrase;
    }

    public ResponsePlan WithText(string text, string? stateVersion = null, IEnumerable<ConversationMenuOption>? choices = null) => new(WorkspaceId, SourceMessageId, stateVersion ?? StateVersion,
        Intent, Act, Operation, Facts, choices ?? Choices, NeededData, TimeZone, Currency, NextQuestion, text, AllowCreativeParaphrase) {
            Realization = text == FallbackText ? Realization : null
        };
}
