using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Infrastructure.Engines.AI;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AuthorizedAgentV2TrialAttribute : FactAttribute
{
    public AuthorizedAgentV2TrialAttribute()
    {
        if (Environment.GetEnvironmentVariable("NEXFLOW_AGENT_V2_TRIAL_AUTHORIZED") != "GROQ_120B_SYNTHETIC_ONE_CONVERSATION"
            || Environment.GetEnvironmentVariable("NEXFLOW_LLM_QUOTAS_VERIFIED") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXFLOW_TEST_GROQ_API_KEY")))
            Skip = "BLOCKED: authorized Groq test credential and verified quota required. No requests performed.";
    }
}

// Development diagnostic, NOT an independent/private benchmark. No legacy, WhatsApp,
// SQL or external Firestore; real model + real read adapters over existing synthetic fixtures.
[Trait("Category", "ProviderIntegration")]
public sealed class AgentV2LiveTrialTests
{
    [AuthorizedAgentV2Trial]
    public async Task Authorized_single_Groq_conversation_records_actual_AgentV2_answers_without_commercial_operations()
    {
        const string model = "openai/gpt-oss-120b";
        var key = Environment.GetEnvironmentVariable("NEXFLOW_TEST_GROQ_API_KEY")!;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "backend", "NexFlow.Tests"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root.FullName, "backend", "TestResults", "AgentV2_20261104"); Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "agentv2-groq-20261104.started");
        Assert.False(File.Exists(marker), "Already started or uncertain: this authorization must not be repeated.");
        var budget = new AgentEvaluationHttpBudget(12, 30_000, new AgentHttpAttempt(marker, "AgentV2-Groq120b-20261104-one-conversation"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Groq:ApiKey"] = key, ["Groq:Model"] = model, ["Groq:TimeoutSeconds"] = "20" }).Build();
        using var http = new HttpClient(budget.Handler());
        var router = new AiRouter([new GroqAiProvider(http, config, NullLogger<GroqAiProvider>.Instance)], NullLogger<AiRouter>.Instance, AiProviderType.Groq);
        var c = new AgentV2RuntimeTests.Case(external: router);
        var turns = new[] {
            "Hola, desde ayer me molesta un diente. ¿Qué atención ofrecen para orientarme?",
            "Estoy cerca de Concepción, ¿cuál de esos servicios me conviene para empezar?",
            "Quieor la evaluacion odontologixa. ¿Cuánto cuesta?",
            "Sí, ayúdame a preparar esa cita en esa sede. Por ahora no la registres."
        };
        string Sanitize(string text) => text.Replace(key, "[REDACTED]", StringComparison.Ordinal)
            .Replace(AssistantConversationTests.Fixture.Phone, "[SYNTHETIC_CLIENT]", StringComparison.Ordinal);
        var observations = new List<object>();
        try {
            for (var i = 0; i < turns.Length; i++) {
                var start = budget.Attempts;
                var answer = await c.Turn(turns[i], "authorized-v2-" + i);
                var state = await c.F.Business.Context();
                observations.Add(new { Turn = i, User = turns[i], Candidate = answer.ProposedAnswer == null ? null : Sanitize(answer.ProposedAnswer.Text),
                    Published = answer.Durable.VerifiedText == null ? null : Sanitize(answer.Durable.VerifiedText), answer.Durable.Accepted,
                    answer.Durable.Outcome, answer.Durable.ModelCalls, answer.Durable.ToolCalls, answer.Durable.ElapsedMilliseconds,
                    HTTPStart = start, HTTPCount = budget.Attempts - start, Trace = answer.Trace,
                    State = new { state.CurrentGoal, state.SelectedServiceId, state.SelectedLocationId, state.AgentV2 } });
                // Only the durably publishable response becomes history. An unsafe
                // candidate cannot be smuggled into the next turn as an assistant fact.
                c.F.History.Add(new MessageRecord { Sender = SenderType.Consumer, Content = turns[i], ExternalMessageId = "authorized-v2-" + i, Timestamp = c.F.Business.Now });
                if (answer.Durable.Publishable) c.F.History.Add(new MessageRecord { Sender = SenderType.AI, Content = answer.Durable.VerifiedText!, Timestamp = c.F.Business.Now });
                if (answer.ProposedAnswer == null) break; // Stop technical failures; no retries or provider repair loop.
            }
        } finally {
            using var report = new FileStream(Path.Combine(directory, "agentv2-live-observed.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var sourceFiles = new[] {
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2Contracts.cs",
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2Tools.cs",
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2Runtime.cs",
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2Ingress.cs",
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2AnswerVerifier.cs",
                "backend/NexFlow.Application/Features/AI/Router/AiRouter.cs",
                "backend/NexFlow.Tests/Concierge/AgentV2LiveTrialTests.cs",
                "backend/NexFlow.Tests/Concierge/AgentV2RuntimeTests.cs"
            }.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root.FullName, p)))));
            JsonSerializer.Serialize(report, new { Engine = "AgentV2", SourceRevision = typeof(AgentV2LiveTrialTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                SourceFiles = sourceFiles,
                CandidateIdentity = "UNCOMMITTED_DIFF_REQUIRES_FREEZE", Provider = "Groq", Model = model, Mode = "RealProviderSyntheticRepositories",
                Observations = observations, budget.Attempts, budget.ReservedTokens, budget.ReportedTokens, budget.UsageResponses,
                Requests = budget.Metadata, CommercialEffects = c.F.Business.Bookings.Count + c.F.Business.Orders.Count,
                Outbound = c.F.Business.Sent.Count, HumanQuality = "NOT_CERTIFIED", AutomaticNaturalPublication = "BLOCKED_INDEPENDENT_VERIFICATION" }, new JsonSerializerOptions { WriteIndented = true });
        }
        Assert.InRange(budget.Attempts, 1, 12); Assert.InRange(budget.ReservedTokens, 1, 30_000);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders); Assert.Empty(c.F.Business.Sent);
        Assert.Equal(0, c.F.Business.AiCalls); // Legacy interpreter never participated.
    }
}
