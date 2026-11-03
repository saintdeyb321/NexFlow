using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed record AgentEvaluationTurn(string Text, AgentDecision Decision, AgentReadKind? Read = null,
    string? Search = null, string? Goal = null, string? Pending = null, string[]? Contains = null,
    string? RemoveModule = null, int AdvanceMinutes = 1, string? ReplayOf = null, string? Outcome = null);
public sealed record AgentEvaluationCase(string Id, string Category, bool InitialDraft, AgentEvaluationTurn[] Turns);
public sealed record AgentEvaluationCorpus(int Version, string Provenance, AgentEvaluationCase[] Conversations);

public static class AgentEvaluationData
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() } };
    public static (AgentEvaluationCorpus Corpus, string Hash) Load()
    {
        var external = Environment.GetEnvironmentVariable("NEXFLOW_LLM_CORPUS");
        byte[] bytes;
        if (external != null) {
            bytes = File.ReadAllBytes(external);
            var expected = Environment.GetEnvironmentVariable("NEXFLOW_LLM_CORPUS_SHA256");
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sealed corpus SHA256 mismatch. Expectations are never supplied to the model.");
        } else {
            using var resource = typeof(AgentEvaluationData).Assembly.GetManifestResourceStream("NexFlow.Tests.Concierge.AgentDevelopmentConversations.json")!;
            using var buffer = new MemoryStream(); resource.CopyTo(buffer); bytes = buffer.ToArray();
        }
        var corpus = JsonSerializer.Deserialize<AgentEvaluationCorpus>(bytes, Json)!;
        if (bytes.Length > 500_000 || corpus.Version != 1 || corpus.Conversations.Length is < 1 or > 60
            || corpus.Conversations.Select(c => c.Id).Distinct().Count() != corpus.Conversations.Length
            || corpus.Conversations.Any(c => c.Id.Length is < 1 or > 80 || c.Id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-')
                || c.Turns.Length is < 3 or > 12 || c.Turns.Any(t => string.IsNullOrWhiteSpace(t.Text) || t.Text.Length > 2000 || t.AdvanceMinutes is < 0 or > 60)))
            throw new InvalidOperationException("Invalid bounded conversation corpus.");
        return (corpus, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    public static IEnumerable<object[]> Cases() => Load().Corpus.Conversations.Select(c => new object[] { c.Id });
}

public sealed class AgentConversationEvaluationTests(ITestOutputHelper output)
{
    [Theory, MemberData(nameof(AgentEvaluationData.Cases), MemberType = typeof(AgentEvaluationData))]
    public async Task Offline_development_conversation_compares_observed_routes_without_claiming_real_NLU(string id)
    {
        var (corpus, hash) = AgentEvaluationData.Load(); var scenario = corpus.Conversations.Single(c => c.Id == id);
        var observations = new List<object>(); var violations = new List<string>();
        foreach (var mode in new[] { "legacy", "shadow", "agent" })
            await Observe(scenario, mode, observations, violations);
        WriteReport(id, new { Version = 1, Mode = "offline", CorpusSHA256 = hash, corpus.Provenance,
            NLU = "NOT_MEASURED_ScriptedInterpretations", HumanQuality = "BLOCKED", ReportedTokens = (long?)null,
            ProviderHTTP = 0, Observations = observations, Violations = violations });
        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }

    private async Task Observe(AgentEvaluationCase scenario, string mode, List<object> observations, List<string> violations)
    {
        var f = new AgentDialogueFixture(); f.ShadowOptions.Enabled = true; f.ShadowOptions.SyntheticWorkspaceId = f.Business.Workspace;
        if (scenario.InitialDraft) await f.Business.SeedDraft("RESERVATION");
        string? previous = null;
        for (var i = 0; i < scenario.Turns.Length; i++) {
            var t = scenario.Turns[i]; f.Business.Now = f.Business.Now.AddMinutes(t.AdvanceMinutes);
            if (t.RemoveModule != null) f.Business.Modules.Remove(t.RemoveModule);
            var source = t.ReplayOf ?? scenario.Id + "-" + i; var inputs = f.Inputs.Count; var legacyCalls = f.Business.AiCalls;
            var reads = f.Business.FactualQueries.Count; var watch = Stopwatch.StartNew(); AgentTurnResult? result = null;
            string? response = null; string? error = null;
            try {
                if (mode == "legacy") {
                    f.Business.InterpretationOverride = () => Task.FromResult(LegacyInterpretation(t));
                    await f.Business.HandleTurn(t.Text, source); response = f.Business.Sent.GetValueOrDefault(source)?.Content;
                } else {
                    if (t.ReplayOf == null) f.Plan(t.Text, t.Decision, t.Read, t.Search);
                    result = mode == "agent" ? await f.Turn(t.Text, source) : await f.Shadow.EvaluateAsync(f.Request(t.Text, source), default);
                    response = mode == "agent" ? result.VerifiedText ?? result.StatusExplanation : result.Proposal?.CandidateResponse;
                    if (mode == "shadow") {
                        f.History.Add(new() { Sender = SenderType.Consumer, ExternalMessageId = source, Content = t.Text, Timestamp = f.Business.Now });
                        if (response != null) f.History.Add(new() { Sender = SenderType.AI, Content = response, Timestamp = f.Business.Now });
                    }
                }
            } catch (Exception ex) { error = ex.GetType().Name; }
            var state = await f.Business.Context();
            var matches = state.CurrentGoal == t.Goal && state.PendingRecoveryAction == t.Pending
                && (t.Contains ?? []).All(part => response?.Contains(part, StringComparison.OrdinalIgnoreCase) == true)
                && (t.Outcome == null || result?.Outcome == t.Outcome) && error == null;
            var effects = f.Business.Bookings.Count + f.Business.Orders.Count;
            if (mode == "agent" && (!matches || effects != 0 || f.Business.Sent.Count != 0))
                violations.Add($"{scenario.Id}/{i}: state/response/effect mismatch ({result?.Outcome ?? error}); goal={state.CurrentGoal}; pending={state.PendingRecoveryAction}");
            observations.Add(new { Mode = mode, Case = scenario.Id, scenario.Category, Turn = i, Input = t.Text, Response = response,
                Outcome = result?.Outcome, Decision = result?.Proposal?.Decision.ToString(), ExpectedDecision = t.Decision.ToString(),
                ActMatchesScript = result?.Proposal?.Decision == t.Decision, PolicyMatches = matches,
                Goal = state.CurrentGoal, Pending = state.PendingRecoveryAction, state.CurrentStep,
                VerifiedRealization = result?.ResponseValidation, CommercialEffects = effects,
                OutboundIntercepted = f.Business.Sent.Count, LogicalCalls = mode == "legacy" ? f.Business.AiCalls - legacyCalls : f.Inputs.Count - inputs,
                ReadQueries = f.Business.FactualQueries.Count - reads, ToolCalls = result?.ToolCalls, ElapsedMs = watch.Elapsed.TotalMilliseconds,
                RepeatedResponse = response != null && response == previous, Error = error });
            previous = response;
        }
        output.WriteLine($"{scenario.Id}/{mode}: {scenario.Turns.Length} observed turns; HTTP=0; real NLU NOT_MEASURED.");
    }

    private static AiInterpretation LegacyInterpretation(AgentEvaluationTurn turn) => new() {
        Intent = turn.Read switch { AgentReadKind.Services => ConversationIntent.ServiceQuery, AgentReadKind.Products => ConversationIntent.ProductQuery,
            AgentReadKind.Locations => ConversationIntent.Location, AgentReadKind.Profile => ConversationIntent.Profile, AgentReadKind.Hours => ConversationIntent.BusinessHours,
            _ => turn.Decision == AgentDecision.PrepareReservation ? ConversationIntent.Reservation : turn.Decision == AgentDecision.HumanAssistance ? ConversationIntent.Support : ConversationIntent.General },
        Act = turn.Decision == AgentDecision.PrepareReservation ? DialogueAct.StartOperation : turn.Decision == AgentDecision.ClarifyCancellationScope ? DialogueAct.Cancellation : DialogueAct.Information,
        Evidence = [new("Act", turn.Text, turn.Decision == AgentDecision.PrepareReservation ? "StartOperation" : turn.Decision == AgentDecision.ClarifyCancellationScope ? "Cancellation" : "Information")]
    };

    internal static void WriteReport(string id, object result)
    {
        var folder = Environment.GetEnvironmentVariable("NEXFLOW_LLM_EVALUATION_OUTPUT"); if (folder == null) return;
        using var file = new FileStream(Path.Combine(folder, id + ".json"), FileMode.CreateNew);
        JsonSerializer.Serialize(file, result, AgentEvaluationData.Json);
    }
}
