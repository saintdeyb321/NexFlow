using NexFlow.Application.Features.Knowledge;

namespace NexFlow.Application.Features.Automation.Agent;

// No workspace argument from the model; no writes, availability or confirmation tools.
public sealed class AgentReadTools(IBusinessReadTools tools) : IAgentReadTools
{
    public async Task<IReadOnlyList<AgentToolResult>> ReadAsync(Guid workspace, IReadOnlyList<AgentToolCall> calls, CancellationToken ct)
    {
        if (calls.Count is < 1 or > 3 || calls.Any(c => !Enum.IsDefined(c.Kind) || c.Search?.Length > 200 || c.LocationId?.Length > 128))
            throw new ArgumentException("Invalid bounded read plan.");
        var turn = tools.Open(workspace); var results = new List<AgentToolResult>();
        foreach (var call in calls)
        {
            var topic = call.Kind switch {
                AgentReadKind.Services => KnowledgeTopic.Services, AgentReadKind.Products => KnowledgeTopic.Products,
                AgentReadKind.Locations => KnowledgeTopic.Locations, AgentReadKind.Profile => KnowledgeTopic.Profile,
                AgentReadKind.Hours => KnowledgeTopic.BusinessHours, _ => throw new ArgumentException("Unknown tool.") };
            var result = await tools.ReadAsync(turn, new KnowledgeRead(topic, call.LocationId, call.Search), ct);
            if (result.WorkspaceId != workspace || result.Source != topic.ToString()
                || call.LocationId != null && result.LocationId != call.LocationId
                || result.Facts.Any(f => f.Sensitive || f.Source != NexFlow.Application.Features.Automation.Dialogue.Contracts.VerifiedFactSource.BusinessRepository))
                throw new InvalidOperationException("Read provenance mismatch.");
            results.Add(new(results.Count, result.Status, result.Source,
                result.Status == BusinessReadStatus.Found ? result.Facts : []));
        }
        return results.AsReadOnly();
    }
}
