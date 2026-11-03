using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Agent;

// Untrusted linguistic proposals; no business commands, prices or consent fields.
public enum AgentCommunicativeGoal { Information, BookingPreparation, Clarification, HumanHelp }
public enum AgentSpeechAct { Explain, Orient, Reassure, Repair, Resume }
public sealed record AgentSlotCandidate(string Value, string Evidence, int? ReferenceIndex = null, string? QuestionId = null);
public sealed record AgentBookingPatch(AgentSlotCandidate? Location = null, AgentSlotCandidate? Service = null,
    AgentSlotCandidate? Date = null, AgentSlotCandidate? Time = null, AgentSlotCandidate? Name = null);
public sealed record AgentFactSelection(int ReadIndex, string EntityId);
public sealed record AgentConversationPlan(AgentCommunicativeGoal Goal, AgentSpeechAct Speech = AgentSpeechAct.Explain,
    string? Need = null, AgentBookingPatch? Booking = null, AgentFactSelection[]? References = null, bool NeedsClarification = false);
public sealed record AgentDraftStage(ConversationContextDto Context, string? Clarification = null,
    IReadOnlyList<ConversationMenuOption>? Choices = null, string? AvailabilityExplanation = null, int ToolCalls = 0);

public interface IAgentBookingDrafts
{
    Task<AgentDraftStage> StageAsync(AgentTurnRequest request, ConversationContextDto origin, AgentBookingPatch patch,
        int remainingTools, CancellationToken ct);
}
