namespace NexFlow.Application.Features.Automation.AgentV2;

// Independent conservative publication gate. Arbitrary prose is NOT certified by
// the model's own citations. Natural candidates remain available only to isolated evaluation.
public sealed class AgentV2AnswerVerifier : IAgentV2AnswerVerifier
{
    public AgentV2Verification Verify(AgentV2Answer answer, IReadOnlyList<AgentV2Fact> facts)
    {
        if (string.IsNullOrWhiteSpace(answer.Text) || answer.Text.Length > 3000 || answer.FactIds == null || answer.FactIds.Length is < 1 or > 20
            || answer.FactIds.Distinct(StringComparer.Ordinal).Count() != answer.FactIds.Length) return new(false, "UnverifiedNaturalLanguage");
        if (facts.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != facts.Count) return new(false, "AmbiguousFacts");
        var indexed = facts.ToDictionary(f => f.Id, StringComparer.Ordinal);
        if (answer.FactIds.Any(id => !indexed.ContainsKey(id))) return new(false, "UnknownFact");
        var exact = string.Join("\n", answer.FactIds.Select(id => indexed[id].Value));
        return answer.Text == exact ? new(true, "ExactAuthorizedFacts", exact) : new(false, "UnverifiedNaturalLanguage");
    }
}
