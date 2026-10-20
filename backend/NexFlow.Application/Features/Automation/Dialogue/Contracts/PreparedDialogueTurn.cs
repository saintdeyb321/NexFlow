using NexFlow.Application.Features.AI.Interpretation;

namespace NexFlow.Application.Features.Automation.Dialogue.Contracts;

public enum DialogueAct { Information, OperationInformation, StartOperation, RequestedData, MultipleData, Correction, InformationalInterruption, Confirmation, Cancellation, HumanAssistance, SemanticAmbiguity, ProviderFailure, BusinessUnavailable }

public sealed record PreparedDialogueTurn(DialogueAct Act, AiInterpretation Interpretation, bool UsedAi = false, string? Response = null);

// Server-owned, ephemeral proposal. Only BookingFlow applies it, using the existing context CAS.
public sealed record BookingDraftProposal(Guid WorkspaceId, string SourceMessageId, string? ExpectedStateVersion,
    TurnUnderstanding Understanding, string? Clarification, IReadOnlyList<string> UnresolvedSlots);

public sealed record SlotReadPlan(bool Locations, bool Services, bool Products)
{
    public static SlotReadPlan All { get; } = new(true, true, true);
}
