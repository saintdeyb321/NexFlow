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
            NexFlow.Application.Features.Automation.ProcessMessage.Services.ConversationDiagnostics.Count("AgentRead", call.Kind.ToString());
            var topic = call.Kind switch {
                AgentReadKind.Services => KnowledgeTopic.Services, AgentReadKind.Products => KnowledgeTopic.Products,
                AgentReadKind.Locations => KnowledgeTopic.Locations, AgentReadKind.Profile => KnowledgeTopic.Profile,
                AgentReadKind.Hours => KnowledgeTopic.BusinessHours, _ => throw new ArgumentException("Unknown tool.") };
            var result = await tools.ReadAsync(turn, new KnowledgeRead(topic, call.LocationId, call.Search), ct);
            if (result.WorkspaceId != workspace || result.Source != topic.ToString()
                || call.LocationId != null && result.LocationId != call.LocationId
                || result.Facts.Any(f => f.Sensitive || f.Source != NexFlow.Application.Features.Automation.Dialogue.Contracts.VerifiedFactSource.BusinessRepository))
                throw new InvalidOperationException("Read provenance mismatch.");
            var facts = result.Status == BusinessReadStatus.Found ? result.Facts : [];
            if (result.Status == BusinessReadStatus.Found && result.Knowledge is { } knowledge) {
                if (topic == KnowledgeTopic.Locations && knowledge.References.Count > 0) facts = knowledge.References.Select(l => new NexFlow.Application.Features.Automation.Dialogue.Contracts.VerifiedFact(
                    "LocationName", l.Name, NexFlow.Application.Features.Automation.Dialogue.Contracts.VerifiedFactSource.BusinessRepository, l.Id)).ToArray();
                if (topic == KnowledgeTopic.Profile && knowledge.Profile is { } profile) facts = [new("BusinessName", profile.Name,
                    NexFlow.Application.Features.Automation.Dialogue.Contracts.VerifiedFactSource.BusinessRepository)];
            }
            results.Add(new(results.Count, result.Status, result.Source, facts));
        }
        return results.AsReadOnly();
    }
}
