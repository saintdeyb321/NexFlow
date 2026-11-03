using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Knowledge;

namespace NexFlow.Application.Features.Automation.Agent;

public sealed class AgentShadowOptions
{
    public bool Enabled { get; set; }
    public Guid SyntheticWorkspaceId { get; set; }
}

// Internal experiment entry point, not an inbound/HTTP route or a write command.
public sealed record AgentTurnRequest(Guid WorkspaceId, string ConversationId, string ClientKey,
    string SourceMessageId, string Text);
public sealed record AgentRecentTurn(string Role, string Text);
public sealed record AgentDraftSnapshot(string? Goal, string? Step, string? LocationId, string? ServiceId,
    string? Date, string? Time, bool HasPatientName);
public sealed record AgentQuestionSnapshot(string Id, string Source, string? PendingAction, string Prompt, DateTime ExpiresAt,
    Guid WorkspaceId, string? ConversationId, ExpectedReply Expected);
public sealed record AgentTurnContext(Guid WorkspaceId, string ConversationId, string SourceMessageId,
    string? StateVersion, string Text, AgentDraftSnapshot Draft, AgentQuestionSnapshot? Question,
    IReadOnlyList<AgentRecentTurn> RecentTurns, IReadOnlyList<string> Capabilities)
{
    public string Language => "es-PE";
    public static AgentTurnContext Capture(AgentTurnRequest request, ConversationContextDto state,
        IEnumerable<AgentRecentTurn> recent, IEnumerable<string> modules, DateTime now)
    {
        var question = ConversationQuestion.Current(request.WorkspaceId, state, now);
        if (question?.ConversationId != null && question.ConversationId != request.ConversationId) question = null;
        return new(request.WorkspaceId, request.ConversationId, request.SourceMessageId, state.StateVersion, request.Text,
            new(state.CurrentGoal, state.CurrentStep, state.SelectedLocationId, state.SelectedServiceId,
                state.TargetDate, state.TargetTime, !string.IsNullOrWhiteSpace(state.RealCustomerName)),
            question == null ? null : new(question.QuestionId, question.SourceMessageId, state.PendingRecoveryAction,
                state.PendingRecoveryAction is "RESERVATION_SCOPE" or "AGENT_CANCEL_SCOPE" ? "¿Te refieres a la reserva en preparación o a una cita registrada?"
                    : question.LinguisticQuestion(state), question.ExpiresAt, question.WorkspaceId, question.ConversationId, question.Expected),
            Array.AsReadOnly(recent.TakeLast(6).Select(t => new AgentRecentTurn(t.Role, t.Text[..Math.Min(t.Text.Length, 400)])).ToArray()),
            Array.AsReadOnly(modules.Select(m => m.ToUpperInvariant())
                .Where(m => m is "SERVICES" or "CATALOG" or "LOCATIONS" or "BUSINESS_HOURS" or "FAQ" or "RESERVATIONS")
                .Distinct().Order(StringComparer.Ordinal).ToArray()));
    }
}

public enum AgentDecision { Information, Clarify, PrepareReservation, ClarifyCancellationScope, ProposeDiscardDraft, HumanAssistance, RetainDraft }
public enum AgentOpening { None, Warm, Acknowledge }
public enum AgentNextQuestion { Information, Service, Location, HumanHelp }
public sealed record AgentNarrative(AgentOpening Opening = AgentOpening.None, AgentNextQuestion? NextQuestion = null);
public enum AgentReadKind { Services, Products, Locations, Profile, Hours }
public sealed record AgentToolCall(AgentReadKind Kind, string? Search = null, string? LocationId = null);
public sealed record AgentToolResult(int Index, BusinessReadStatus Status, string Source, IReadOnlyList<VerifiedFact> Facts);
public sealed record AgentProposal(AgentDecision Decision, string Evidence, string CandidateResponse,
    string? QuestionId = null, AgentToolCall[]? Tools = null, int[]? FactReferences = null, AgentNarrative? Narrative = null);
public sealed record AgentTurnResult(bool Accepted, string Outcome, AgentProposal? Proposal, IReadOnlyList<AgentToolResult> Reads,
    int ModelCalls, long ElapsedMilliseconds, AiFailureKind Failure = AiFailureKind.None,
    string? VerifiedText = null, bool Recovered = false, string? StatusExplanation = null, IReadOnlyList<string>? RequiredModules = null, int ToolCalls = 0)
{
    // Free wording is evaluated, never certified by another LLM or sent to a consumer.
    public bool Publishable => !string.IsNullOrWhiteSpace(VerifiedText);
    public string ResponseValidation => Publishable ? "ServerRealizedVerifiedSegments" : "UnverifiedShadowProposal";
    public static AgentTurnResult Rejected(string reason, int calls = 0, long elapsed = 0, AiFailureKind failure = AiFailureKind.None)
        => new(false, reason, null, [], calls, elapsed, failure);
}
public interface IConversationalAgent
{
    Task<AgentTurnResult> EvaluateAsync(AgentTurnRequest request, CancellationToken ct);
}
public interface IAgentReadTools
{
    Task<IReadOnlyList<AgentToolResult>> ReadAsync(Guid workspace, IReadOnlyList<AgentToolCall> calls, CancellationToken ct);
}

public enum AgentEvaluationClaim { Started, Duplicate, Conflict }
// At-most-once AI admission metadata only; never a draft or a business receipt.
public interface IAgentEvaluationClaims
{
    Task<AgentEvaluationClaim> TryBeginAsync(AgentTurnRequest request, CancellationToken ct);
}
