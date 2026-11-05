using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.ProcessMessage;
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
        var reportPath = Path.Combine(directory, "agentv2-live-observed.json");
        Assert.False(File.Exists(reportPath), "An existing evaluation report requires review before any further HTTP.");
        var budget = new AgentEvaluationHttpBudget(12, 30_000, new AgentHttpAttempt(marker, "AgentV2-Groq120b-20261104-one-conversation"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Groq:ApiKey"] = key, ["Groq:Model"] = model, ["Groq:TimeoutSeconds"] = "20" }).Build();
        using var http = new HttpClient(budget.Handler());
        var router = new AiRouter([new GroqAiProvider(http, config, NullLogger<GroqAiProvider>.Instance)], NullLogger<AiRouter>.Instance, AiProviderType.Groq);
        var c = new AgentV2RuntimeTests.Case(external: router, channel: true);
        c.F.Business.Now = DateTime.UtcNow;
        c.F.Business.ProfileReads.Setup(p => p.GetProfileAsync(c.F.Business.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NexFlow.Application.Features.Business.BusinessProfileDto("Clínica DEMO sintética", "", "", "", "Consultorio dental ficticio para ensayos aislados, sin diagnóstico por chat."));
        c.F.Business.Services[0].Description = "Consulta inicial de valoración profesional para orientar los siguientes pasos; no constituye diagnóstico por chat.";
        c.F.Business.Services[1].Description = "Procedimiento que requiere valoración previa del profesional; no se indica automáticamente por una molestia.";
        var channel = new AgentDemoChannel(c.F.DemoPolicy, c.Runtime, c.F.Journal, c.F.DemoRepository!, c.F.Business.Contexts,
            c.F.Business.EntitlementReads.Object, c.F.DemoOutbound!, new HumanHandoffService(c.F.DemoRepository!, c.F.Business.Contexts));
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
                // Synthetic clock pacing preserves the existing RPM policy. This
                // is not a throughput measurement or a production quota change.
                if (i > 0) c.F.Business.Now = c.F.Business.Now.AddMinutes(1);
                var start = budget.Attempts;
                await c.F.DemoRepository!.AddMessageAsync(c.F.Business.Workspace, "chat", new MessageRecord { Id = "authorized-v2-" + i,
                    Sender = SenderType.Consumer, Content = turns[i], ExternalMessageId = "authorized-v2-" + i, Timestamp = c.F.Business.Now }, default);
                var answer = await c.Turn(turns[i], "authorized-v2-" + i);
                string? published = null;
                if (answer.Durable.Publishable) {
                    var conversation = await c.F.DemoRepository.GetConversationAsync(c.F.Business.Workspace, "chat", default);
                    var sent = await channel.RespondAsync(c.F.Business.Workspace, AssistantConversationTests.Fixture.Phone,
                        new ProcessIncomingMessageCommand("instance", AssistantConversationTests.Fixture.Phone, "Synthetic", turns[i], "authorized-v2-" + i, false, c.F.Business.Workspace), conversation!, default);
                    published = sent.Content;
                }
                var state = await c.F.Business.Context();
                observations.Add(new { Turn = i, User = turns[i], Candidate = answer.ProposedAnswer == null ? null : Sanitize(answer.ProposedAnswer.Text),
                    Published = published == null ? null : Sanitize(published), answer.Durable.Accepted,
                    answer.Durable.Outcome, answer.Durable.ModelCalls, answer.Durable.ToolCalls, answer.Durable.ElapsedMilliseconds,
                    HTTPStart = start, HTTPCount = budget.Attempts - start, Trace = answer.Trace,
                    Tools = answer.Tools?.Select(t => new { t.CallId, t.Name, t.Status, Facts = t.Facts.Select(f => f with { Value = Sanitize(f.Value) }) }).ToArray(),
                    State = new { state.CurrentGoal, state.SelectedServiceId, state.SelectedLocationId, state.AgentV2 } });
                // Actual journal/outbound history is used on the next turn. The
                // raw candidate never becomes history unless it passes verification.
                if (answer.ProposedAnswer == null) break; // Stop technical failures; no retries or provider repair loop.
            }
        } finally {
            using var report = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var sourceFiles = new[] {
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2Contracts.cs",
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2Tools.cs",
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2Runtime.cs",
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2Ingress.cs",
                "backend/NexFlow.Application/Features/Automation/AgentV2/AgentV2AnswerVerifier.cs",
                "backend/NexFlow.Application/Features/Automation/Agent/AgentBookingDrafts.cs",
                "backend/NexFlow.Application/Features/AI/Router/AiRouter.cs",
                "backend/NexFlow.Tests/Concierge/AgentV2LiveTrialTests.cs",
                "backend/NexFlow.Tests/Concierge/AgentV2RuntimeTests.cs"
            }.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root.FullName, p)))));
            JsonSerializer.Serialize(report, new { Engine = "AgentV2", SourceRevision = typeof(AgentV2LiveTrialTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                SourceFiles = sourceFiles,
                CandidateIdentity = "UNCOMMITTED_DIFF_REQUIRES_FREEZE", Provider = "Groq", Model = model, Mode = "RealProviderSyntheticRepositories",
                ClockPolicy = "SyntheticBusinessClock_OneMinuteBetweenUserTurns_NotThroughputCertification",
                Observations = observations, budget.Attempts, budget.ReservedTokens, budget.ReportedTokens, budget.UsageResponses,
                Requests = budget.Metadata, CommercialEffects = c.F.Business.Bookings.Count + c.F.Business.Orders.Count,
                Outbound = c.F.WireCalls, Channel = "ExistingOutbox_InterceptedSyntheticTransport_NoWhatsApp",
                HumanQuality = "NOT_CERTIFIED", AutomaticNaturalPublication = "ANCHORS_PLUS_SEPARATE_SEMANTIC_REVIEW_WITH_RESIDUAL_RISK" }, new JsonSerializerOptions { WriteIndented = true });
        }
        Assert.InRange(budget.Attempts, 1, 12); Assert.InRange(budget.ReservedTokens, 1, 30_000);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders); Assert.Empty(c.F.Business.Sent);
        Assert.InRange(c.F.WireCalls, 0, turns.Length);
        Assert.Equal(0, c.F.Business.AiCalls); // Legacy interpreter never participated.
    }
}
