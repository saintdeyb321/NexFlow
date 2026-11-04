using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Infrastructure.Engines.AI;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AuthorizedAgentEvaluationAttribute : FactAttribute
{
    public AuthorizedAgentEvaluationAttribute()
    {
        if (Environment.GetEnvironmentVariable("NEXFLOW_LLM_LIVE_AUTHORIZED") != "1"
            || Environment.GetEnvironmentVariable("NEXFLOW_LLM_QUOTAS_VERIFIED") != "1")
            Skip = "NOT_RUN: explicit live authorization, verified quotas and synthetic credentials required.";
    }
}

// Explicit opt-in only; excluded from G8 by its existing ProviderIntegration filter.
[Trait("Category", "ProviderIntegration")]
public sealed class AgentLiveEvaluationTests
{
    [AuthorizedAgentEvaluation]
    public async Task Authorized_complete_conversation_uses_real_router_with_shared_HTTP_budget()
    {
        var (corpus, hash) = AgentEvaluationData.Load();
        Assert.Equal(hash, Environment.GetEnvironmentVariable("NEXFLOW_LLM_CORPUS_SHA256"), ignoreCase: true);
        Assert.NotNull(Environment.GetEnvironmentVariable("NEXFLOW_LLM_EVALUATION_OUTPUT"));
        var scenario = corpus.Conversations.Single(c => c.Id == Environment.GetEnvironmentVariable("NEXFLOW_LLM_LIVE_CASE"));
        var budget = new AgentEvaluationHttpBudget(
            int.Parse(Environment.GetEnvironmentVariable("NEXFLOW_LLM_MAX_HTTP")!),
            long.Parse(Environment.GetEnvironmentVariable("NEXFLOW_LLM_MAX_TOKEN_RESERVATION")!), AgentHttpAttempt.FromEnvironment());
        var settings = new Dictionary<string, string?>();
        foreach (var name in new[] { "Groq", "Gemini" }) {
            var prefix = "NEXFLOW_TEST_" + name.ToUpperInvariant();
            var key = Environment.GetEnvironmentVariable(prefix + "_API_KEY"); var model = Environment.GetEnvironmentVariable(prefix + "_MODEL");
            Assert.False(string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model), "Authorized synthetic provider environment required; no application settings fallback.");
            settings[name + ":ApiKey"] = key; settings[name + ":Model"] = model; settings[name + ":TimeoutSeconds"] = "8";
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var groq = new HttpClient(budget.Handler()); using var gemini = new HttpClient(budget.Handler());
        var router = new AiRouter([new GroqAiProvider(groq, config, NullLogger<GroqAiProvider>.Instance),
            new GeminiAiProvider(gemini, config, NullLogger<GeminiAiProvider>.Instance)], NullLogger<AiRouter>.Instance);
        var trace = new AgentEvaluationTrace(AssistantConversationTests.Fixture.Phone,
            settings["Groq:ApiKey"], settings["Gemini:ApiKey"]);
        var f = new AgentDialogueFixture(router, evidence: trace); if (scenario.InitialDraft) await f.Business.SeedDraft("RESERVATION");
        var rows = new List<object>(); var failures = new List<string>();
        for (var i = 0; i < scenario.Turns.Length; i++) {
            var t = scenario.Turns[i]; f.Business.Now = f.Business.Now.AddMinutes(t.AdvanceMinutes);
            if (t.RemoveModule != null) f.Business.Modules.Remove(t.RemoveModule);
            var before = trace.Context(f.Business.Workspace, await f.Business.Context(), f.Business.Now); var start = trace.Position;
            var httpStart = budget.Attempts;
            // No Plan()/scripted responses. Only authentic text goes to AiRouter.
            var result = await f.Turn(t.Text, t.ReplayOf ?? scenario.Id + "-" + i); var state = await f.Business.Context();
            var after = trace.Context(f.Business.Workspace, state, f.Business.Now);
            var stateCorrect = state.CurrentGoal == t.Goal && state.PendingRecoveryAction == t.Pending;
            var suppressed = t.Outcome == "NoExperimentalResponse" && result.Outcome == t.Outcome && state.Mode == "Human";
            var correct = stateCorrect && (suppressed || result.Proposal?.Decision == t.Decision);
            if (!correct || f.Business.Bookings.Count + f.Business.Orders.Count + f.Business.Sent.Count != 0) failures.Add(scenario.Id + "/" + i);
            rows.Add(new { Turn = i, Act = result.Proposal?.Decision.ToString(), ExpectedAct = t.Decision.ToString(), StateCorrect = stateCorrect, ActCorrect = correct,
                PublicText = trace.PublicText(result), result.Outcome, result.ModelCalls, result.ElapsedMilliseconds,
                Trace = trace.Turn(t.Text, before, after, result, start),
                HTTPStart = httpStart, HTTPCount = budget.Attempts - httpStart,
                ResponseValidation = suppressed ? "HumanSuppressed" : result.ResponseValidation,
                CommercialEffects = f.Business.Bookings.Count + f.Business.Orders.Count, OutboundEffects = f.Business.Sent.Count });
        }
        AgentConversationEvaluationTests.WriteReport("live-" + scenario.Id, new { Version = 3, Case = scenario.Id, AttemptId = Environment.GetEnvironmentVariable("NEXFLOW_LLM_ATTEMPT_ID"), CorpusSHA256 = hash, Mode = "live", Observations = rows,
            HTTPAttempts = budget.Attempts, TokenReservation = budget.ReservedTokens, ReportedTokens = budget.ReportedTokens,
            UsageResponses = budget.UsageResponses, Requests = budget.Metadata, Failures = failures, HumanQuality = "BLOCKED",
            CommercialAcceptance = "NOT_CERTIFIED", Cost = "UNKNOWN_NoVerifiedTariff" });
        Assert.Empty(failures);
    }
}

// Test-runner budget, not production quota. Reservations are not refunded on
// timeout/429; all primary/fallback requests share the same counter.
internal sealed class AgentEvaluationHttpBudget(int maxAttempts, long maxReservedTokens, AgentHttpAttempt? attempt = null)
{
    private readonly object _sync = new();
    public int Attempts { get; private set; }
    public long ReservedTokens { get; private set; }
    public long? ReportedTokens { get; private set; }
    public int UsageResponses { get; private set; }
    public List<object> Metadata { get; } = [];
    public HttpMessageHandler Handler(HttpMessageHandler? inner = null)
        => new Guard(this, inner ?? new HttpClientHandler { AllowAutoRedirect = false }, inner == null ? "ProviderHttp" : "InterceptedHttp");
    private sealed class Guard(AgentEvaluationHttpBudget budget, HttpMessageHandler inner, string transport) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (maxInvalid(budget) || request.RequestUri is not { Scheme: "https", Host: "api.groq.com" or "generativelanguage.googleapis.com" })
                throw new AiBudgetDeniedException("EvaluationConfiguration");
            var body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            var size = body.Length;
            var awaitModel = "UNKNOWN";
            if (request.RequestUri.Host == "api.groq.com") {
                try { using var json = JsonDocument.Parse(body); if (json.RootElement.TryGetProperty("model", out var model)) awaitModel = model.GetString() ?? "UNKNOWN"; }
                catch (JsonException) { /* Missing metadata can never certify real NLU. */ }
            } else awaitModel = Uri.UnescapeDataString(request.RequestUri.AbsolutePath.Split('/').Last().Split(':')[0]);
            var reserved = size + 1024L; // conservative byte bound, not a fabricated usage measurement
            lock (budget._sync) {
                if (size > 32_000 || budget.Attempts >= budget.AttemptLimit || budget.ReservedTokens + reserved > budget.TokenLimit)
                    throw new AiBudgetDeniedException("EvaluationBudget");
                budget.StartAttempt(transport == "ProviderHttp"); // Immediately before the first possible HTTP.
                budget.Attempts++; budget.ReservedTokens += reserved;
            }
            var started = System.Diagnostics.Stopwatch.GetTimestamp(); int? status = null; long? tokens = null;
            try {
                var response = await base.SendAsync(request, ct); status = (int)response.StatusCode;
                await response.Content.LoadIntoBufferAsync(65_536, ct);
                try {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (json.RootElement.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var total)
                        || json.RootElement.TryGetProperty("usageMetadata", out usage) && usage.TryGetProperty("totalTokenCount", out total))
                        if (total.TryGetInt64(out var n) && n >= 0) tokens = n;
                } catch (JsonException) { /* usage remains unavailable */ }
                return response;
            } finally {
                lock (budget._sync) {
                    if (tokens.HasValue) { budget.ReportedTokens = (budget.ReportedTokens ?? 0) + tokens; budget.UsageResponses++; }
                    budget.Metadata.Add(new { Provider = request.RequestUri!.Host == "api.groq.com" ? "Groq" : "Gemini", Model = awaitModel,
                        Transport = transport, Status = status,
                        ElapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, ReservedTokens = reserved, ReportedTokens = tokens });
                }
            }
        }
        private static bool maxInvalid(AgentEvaluationHttpBudget b) => b.AttemptLimit is < 1 or > 12 || b.TokenLimit is < 2048 or > 100_000;
    }
    private int AttemptLimit => maxAttempts;
    private long TokenLimit => maxReservedTokens;
    private void StartAttempt(bool real)
    {
        if (real && attempt == null) throw new AiBudgetDeniedException("EvaluationClaimRequired");
        attempt?.Start();
    }
}

internal sealed class AgentHttpAttempt(string file, string identity)
{
    private readonly object _sync = new(); private bool _started;
    public static AgentHttpAttempt FromEnvironment()
    {
        var id = Environment.GetEnvironmentVariable("NEXFLOW_LLM_ATTEMPT_ID") ?? "";
        var path = Environment.GetEnvironmentVariable("NEXFLOW_LLM_CLAIM_FILE") ?? "";
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "backend", "NexFlow.Tests"))) root = root.Parent;
        if (root == null || id.Length != 64 || !id.All(char.IsAsciiHexDigit)
            || Path.GetFullPath(path) != Path.Combine(root.FullName, "backend", "TestResults", "llm-live-v2-" + id + ".json"))
            throw new AiBudgetDeniedException("EvaluationClaimConfiguration");
        return new(path, id);
    }
    public void Start()
    {
        lock (_sync) {
            if (_started) return;
            try {
                using var fileStream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(fileStream, new { state = "Started", identity }); fileStream.Flush(flushToDisk: true);
                _started = true;
            } catch (IOException) { throw new AiBudgetDeniedException("EvaluationAlreadyStartedOrUncertain"); }
        }
    }
}
