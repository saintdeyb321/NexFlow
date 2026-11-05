using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.ProcessMessage;

namespace NexFlow.Application.Features.Automation.AgentV2;

public sealed class AgentV2Options
{
    public Dictionary<Guid, AgentV2Workspace> Workspaces { get; set; } = [];
}
public sealed class AgentV2Workspace
{
    public int Revision { get; set; } = 1;
    public bool Enabled { get; set; }
    public string[] CapabilityPacks { get; set; } = [];
    // Explicit mapping of existing SERVICES records to room types, never invented inventory.
    public string[] RoomTypeServiceIds { get; set; } = [];
}
public sealed record AgentV2Memory(int Revision, string Goal, string[] ReferencedEntities);
public sealed record AgentV2ToolCall(string Id, string Name, JsonElement Arguments);
public sealed record AgentV2Answer(string Text, string[] FactIds);
public sealed record AgentV2ModelTurn(AgentV2ToolCall[] Calls, AgentV2Answer? Answer, string Goal = "Information");
public sealed record AgentV2Fact(string Id, string Key, string Value, string Source, string? EntityId = null);
public sealed record AgentV2ToolResult(string CallId, string Name, string Status, IReadOnlyList<AgentV2Fact> Facts,
    string[] RequiredModules, ConversationContextDto? Prepared = null);
public sealed record AgentV2ToolDefinition(string Name, string Description, JsonElement Parameters);
public sealed record AgentV2Verification(bool Accepted, string Reason, string? Text = null);
public sealed record AgentV2Observation(string Engine, string Build, int Revision, string Stage, string Outcome, int ModelCalls, int ToolCalls, string? Tool = null);
public sealed record AgentV2Evaluation(AgentTurnResult Durable, AgentV2Answer? ProposedAnswer, IReadOnlyList<AgentV2Observation> Trace);

public interface IAgentV2Ingress
{
    Task<bool> TryHandleAsync(Guid workspace, string phone, ProcessIncomingMessageCommand input, CancellationToken ct);
}
public interface IAgentV2Runtime : IExperimentalAgentRuntime
{
    Task<AgentV2Evaluation> EvaluateAsync(AgentTurnRequest request, CancellationToken ct);
}
public interface IAgentV2AnswerVerifier
{
    AgentV2Verification Verify(AgentV2Answer answer, IReadOnlyList<AgentV2Fact> facts);
}
