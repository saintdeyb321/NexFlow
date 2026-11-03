using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;

namespace NexFlow.Application.Features.Automation.Agent;

public sealed class AgentDialogueOptions
{
    public bool Enabled { get; set; }
    public Guid SyntheticWorkspaceId { get; set; }
    public int ExecutionSeconds { get; set; } = 30;
    public int ResultRetentionHours { get; set; } = 24;
}

// Only trusted backend evaluation code can establish this scope. There is no
// HTTP/header/webhook switch and no worker establishes it in production.
public sealed class AgentExperimentScope : IDisposable
{
    private static readonly AsyncLocal<AgentExperimentScope?> Slot = new();
    private readonly AgentExperimentScope? _previous = Slot.Value;
    private AgentExperimentScope(Guid workspace) { Workspace = workspace; Slot.Value = this; }
    public Guid Workspace { get; }
    public List<AgentTurnResult> Results { get; } = [];
    public static AgentExperimentScope? Current => Slot.Value;
    public static AgentExperimentScope Begin(Guid workspace) => workspace == Guid.Empty ? throw new ArgumentException("Workspace required.") : new(workspace);
    public void Dispose() => Slot.Value = _previous;
}

public enum AgentJournalStatus { Started, Prepared, Failed, Conflict }
public sealed record AgentTurnLease(string Key, string Fingerprint, string Fence, string? ContextVersion,
    string ContextFingerprint, DateTime Deadline);
public sealed record AgentJournalEntry(AgentJournalStatus Status, AgentTurnLease? Lease = null,
    AgentTurnResult? Result = null, string? AppliedVersion = null);
public interface IAgentTurnJournal
{
    Task<AgentJournalEntry> ObserveAsync(AgentTurnRequest request, CancellationToken ct);
    Task<AgentJournalEntry> BeginAsync(AgentTurnRequest request, ConversationContextDto origin, AgentDialogueOptions options, CancellationToken ct);
    Task<AgentJournalEntry> CompleteAsync(AgentTurnRequest request, AgentTurnLease lease, ConversationContextDto? next,
        AgentTurnResult safeResult, CancellationToken ct);
}
public interface IExperimentalAgentRuntime
{
    bool Handles(Guid workspace);
    Task<AgentTurnResult> HandleAsync(AgentTurnRequest request, CancellationToken ct);
}

public sealed class AgentOwner(LlmFirstShadowAgent shadow, IExperimentalAgentRuntime experiment) : IConversationalAgent
{
    public Task<AgentTurnResult> EvaluateAsync(AgentTurnRequest request, CancellationToken ct)
        => experiment.Handles(request.WorkspaceId) ? experiment.HandleAsync(request, ct) : shadow.EvaluateAsync(request, ct);
}

public static class AgentSafeReplies
{
    public const string Pending = "Estoy revisando esta consulta. Todavía no he preparado una respuesta; puedes volver a consultarla en un momento.";
    public const string Interrupted = "No pude terminar esa respuesta. Conservamos lo que estabas preparando. Puedes enviar una nueva consulta o pedir ayuda al negocio.";
    public const string Stale = "La conversación ya avanzó y esa respuesta dejó de estar vigente. Puedes continuar con una nueva consulta.";
    public static AgentTurnResult Failure(string outcome, string text = Interrupted) => AgentTurnResult.Rejected(outcome) with { VerifiedText = text };
}
