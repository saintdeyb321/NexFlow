using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Knowledge;

namespace NexFlow.Tests.Concierge;

// Evaluation-only observer. Never serialize a prompt, provider body, full DTO or request.
internal sealed class AgentEvaluationTrace(params string?[] privateValues)
{
    private sealed record Model(string Phase, string Outcome, string? Decision, AgentToolCall[] Tools, AgentToolResult[] Reads, int[] References);
    private sealed record Read(string Kind, BusinessReadResult? Result, string? Error);
    private readonly List<Model> _models = [];
    private readonly List<Read> _reads = [];
    private readonly HashSet<string> _private = privateValues.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToHashSet(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    public (int Models, int Reads) Position => (_models.Count, _reads.Count);
    public void Hide(string? value) { if (!string.IsNullOrWhiteSpace(value)) _private.Add(value); }
    private static string? Hash(string? value) => value == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private string? Clean(string? value)
    {
        if (value == null) return null;
        foreach (var secret in _private.OrderByDescending(s => s.Length)) value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        value = value[..Math.Min(value.Length, 2000)];
        // Privacy filters, not conversational interpretation. Preserve ISO dates/prices.
        value = Regex.Replace(value, @"\+\d[\d ()-]{5,25}\d\b|\b\d{9,15}\b|\b\d{3}[ -]\d{3}[ -]\d{3}\b", "[CONTACT]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return Regex.Replace(value, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "[CONTACT]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
    public int BeginModel(string input)
    {
        using var document = JsonDocument.Parse(input);
        var phase = document.RootElement.GetProperty("Phase").GetString();
        // Capture the authorized reads actually presented to this model call;
        // the production authority deliberately removes them from durable results.
        var reads = document.RootElement.TryGetProperty("Reads", out var value) ? JsonSerializer.Deserialize<AgentToolResult[]>(value, Json) ?? [] : [];
        _models.Add(new(phase is "Decision" or "AfterReads" ? phase : "Unknown", "Started", null, [], reads, []));
        return _models.Count - 1;
    }
    public void CompleteModel(int index, string response)
    {
        try {
            var proposal = response.Length <= 8000 ? JsonSerializer.Deserialize<AgentProposal>(response, Json) : null;
            _models[index] = _models[index] with { Outcome = proposal == null ? "InvalidJson" : "Parsed",
                Decision = proposal != null && Enum.IsDefined(proposal.Decision) ? proposal.Decision.ToString() : null,
                Tools = proposal?.Tools?.Take(4).ToArray() ?? [], References = proposal?.FactReferences ?? [] };
        } catch (JsonException) { _models[index] = _models[index] with { Outcome = "InvalidJson" }; }
    }
    public void FailModel(int index) => _models[index] = _models[index] with { Outcome = "ProviderError" };
    public IBusinessReadTools Observe(IBusinessReadTools inner) => new ObservedReads(inner, this);
    private sealed class ObservedReads(IBusinessReadTools inner, AgentEvaluationTrace owner) : IBusinessReadTools
    {
        public BusinessReadTurn Open(Guid workspace) => inner.Open(workspace);
        public async Task<BusinessReadResult> ReadAsync(BusinessReadTurn turn, BusinessReadRequest request, CancellationToken ct)
        {
            var index = owner._reads.Count;
            var kind = request is KnowledgeRead knowledge ? knowledge.Topic.ToString() : request is AvailabilityRead ? "Availability" : "Unknown";
            owner._reads.Add(new(kind, null, null));
            try {
                var result = await inner.ReadAsync(turn, request, ct);
                owner._reads[index] = new(kind, result, null); return result;
            } catch { owner._reads[index] = new(kind, null, "ReadFailed"); throw; }
        }
    }
    public object Context(Guid workspace, ConversationContextDto state, DateTime now)
    {
        Hide(state.RealCustomerName);
        var question = ConversationQuestion.Current(workspace, state, now);
        return new { Goal = Clean(state.CurrentGoal), Step = Clean(state.CurrentStep), Mode = Clean(state.Mode),
            VersionHash = Hash(state.StateVersion), PendingAction = Clean(state.PendingRecoveryAction),
            LocationId = Clean(state.SelectedLocationId), ServiceId = Clean(state.SelectedServiceId),
            Date = Clean(state.TargetDate), Time = Clean(state.TargetTime), HasPatientName = !string.IsNullOrWhiteSpace(state.RealCustomerName),
            Question = question == null ? null : new { IdHash = Hash(question.QuestionId), SourceHash = Hash(question.SourceMessageId),
                Expected = question.Expected.ToString(), Text = Clean(question.LinguisticQuestion(state)), question.ExpiresAt } };
    }
    private object Facts(IEnumerable<VerifiedFact> facts) => facts.Where(f => !f.Sensitive).Take(30).Select(f => new {
        Key = Clean(f.Key), Value = f.Key == "PriceMinorUnits" && f.Source == VerifiedFactSource.BusinessRepository
            && f.Value.Length is > 0 and <= 18 && f.Value.All(char.IsAsciiDigit) ? f.Value : Clean(f.Value),
        Source = f.Source.ToString(), EntityId = Clean(f.EntityId) }).ToArray();
    public object Turn(string input, object before, object after, AgentTurnResult result, (int Models, int Reads) start)
        => new { SyntheticOnly = true, InputText = Clean(input), InputSha256 = Hash(input), Before = before, After = after,
            ModelDecisions = _models.Skip(start.Models).Select(m => new { m.Phase, m.Outcome, m.Decision,
                Tools = m.Tools.Select(t => new { Kind = Enum.IsDefined(t.Kind) ? t.Kind.ToString() : "Unknown", Search = Clean(t.Search), LocationId = Clean(t.LocationId) }).ToArray() }).ToArray(),
            ExecutedTools = _reads.Skip(start.Reads).Select(r => new { r.Kind, Status = r.Result?.Status.ToString() ?? "Error", r.Error,
                Facts = Facts(r.Result?.Facts ?? []), SensitiveFactsOmitted = r.Result?.Facts.Count(f => f.Sensitive) ?? 0 }).ToArray(),
            ResultReads = _models.Skip(start.Models).SelectMany(m => m.Reads).GroupBy(r => r.Index).Select(g => g.Last()).Select(r => new { r.Index, Status = r.Status.ToString(), Source = Clean(r.Source), Facts = Facts(r.Facts),
                SensitiveFactsOmitted = r.Facts.Count(f => f.Sensitive) }).ToArray(),
            FactReferences = _models.Skip(start.Models).LastOrDefault()?.References ?? [], result.Recovered, result.ToolCalls,
            Failure = result.Failure.ToString() };
    public string? PublicText(AgentTurnResult result) => Clean(result.VerifiedText);
}
