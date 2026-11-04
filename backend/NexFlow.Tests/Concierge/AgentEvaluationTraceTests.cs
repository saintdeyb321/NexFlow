using System.Text.Json;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Knowledge;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class AgentEvaluationTraceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Multi_turn_capture_observes_model_plans_real_read_results_and_current_questions_without_effects()
    {
        var trace = new AgentEvaluationTrace(AssistantConversationTests.Fixture.Phone); var f = new AgentDialogueFixture(evidence: trace);
        f.Plan("Quiero preparar una reserva", AgentDecision.PrepareReservation);
        var before = trace.Context(f.Business.Workspace, await f.Business.Context(), f.Business.Now); var start = trace.Position;
        var prepared = await f.Turn("Quiero preparar una reserva", "trace-prepare");
        var after = trace.Context(f.Business.Workspace, await f.Business.Context(), f.Business.Now);
        var first = JsonSerializer.SerializeToElement(trace.Turn("Quiero preparar una reserva", before, after, prepared, start));
        Assert.Equal(JsonValueKind.Null, first.GetProperty("Before").GetProperty("Goal").ValueKind);
        Assert.Equal("RESERVATION", first.GetProperty("After").GetProperty("Goal").GetString());
        Assert.Equal(64, first.GetProperty("After").GetProperty("Question").GetProperty("IdHash").GetString()!.Length);
        f.Plan("¿Qué servicios tienen?", AgentDecision.Information, AgentReadKind.Services);
        before = after; start = trace.Position;
        var response = await f.Turn("¿Qué servicios tienen?", "trace-info");
        after = trace.Context(f.Business.Workspace, await f.Business.Context(), f.Business.Now);
        var second = JsonSerializer.SerializeToElement(trace.Turn("¿Qué servicios tienen?", before, after, response, start));
        var models = second.GetProperty("ModelDecisions"); Assert.Equal(2, models.GetArrayLength());
        Assert.Equal("Services", models[0].GetProperty("Tools")[0].GetProperty("Kind").GetString());
        Assert.Equal(0, models[1].GetProperty("Tools").GetArrayLength());
        var executed = Assert.Single(second.GetProperty("ExecutedTools").EnumerateArray());
        Assert.Equal("Services", executed.GetProperty("Kind").GetString()); Assert.Equal("Found", executed.GetProperty("Status").GetString());
        Assert.Contains(executed.GetProperty("Facts").EnumerateArray(), fact => fact.GetProperty("Key").GetString() == "PriceMinorUnits" && fact.GetProperty("Value").GetString() == "6000");
        Assert.Equal("RESERVATION", second.GetProperty("After").GetProperty("Goal").GetString());
        Assert.NotEmpty(second.GetProperty("ResultReads").EnumerateArray()); Assert.Equal(0, second.GetProperty("FactReferences")[0].GetInt32());
        Assert.Contains("60.00", trace.PublicText(response)); Assert.Equal(3, f.Inputs.Count);
        Assert.Empty(f.Business.Bookings); Assert.Empty(f.Business.Orders); Assert.Empty(f.Business.Sent);
        // Cross-language protocol evidence, synthetic only; not a provider evaluation.
        output.WriteLine("TRACE_CONTRACT_JSON:" + JsonSerializer.Serialize(new { Trace = second, response.ModelCalls, PublicText = trace.PublicText(response) }));
    }

    [Fact]
    public async Task Replay_reports_reused_public_text_without_inventing_a_new_model_call_or_tool_execution()
    {
        var trace = new AgentEvaluationTrace(); var f = new AgentDialogueFixture(evidence: trace);
        f.Plan("Servicios", AgentDecision.Information, AgentReadKind.Services); var original = await f.Turn("Servicios", "trace-replay");
        var start = trace.Position; var before = trace.Context(f.Business.Workspace, await f.Business.Context(), f.Business.Now);
        var result = await f.Turn("Servicios", "trace-replay");
        var captured = JsonSerializer.SerializeToElement(trace.Turn("Servicios", before,
            trace.Context(f.Business.Workspace, await f.Business.Context(), f.Business.Now), result, start));
        Assert.True(result.Recovered); Assert.True(captured.GetProperty("Recovered").GetBoolean());
        Assert.Equal(0, captured.GetProperty("ModelDecisions").GetArrayLength()); Assert.Equal(0, captured.GetProperty("ExecutedTools").GetArrayLength());
        Assert.Empty(captured.GetProperty("ResultReads").EnumerateArray()); Assert.Equal(original.VerifiedText, result.VerifiedText); Assert.Equal(2, f.Inputs.Count);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Invalid_JSON_and_provider_errors_are_observed_without_serializing_the_response_or_exception(bool providerError)
    {
        var trace = new AgentEvaluationTrace(); var router = new Mock<IAiRouter>(MockBehavior.Strict);
        router.Setup(r => r.ExecuteTaskAsync(It.IsAny<AiTaskType>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns(() => providerError ? Task.FromException<string>(new HttpRequestException("private-error-body")) : Task.FromResult("private-invalid-body"));
        var f = new AgentDialogueFixture(router.Object, evidence: trace); var before = trace.Context(f.Business.Workspace, await f.Business.Context(), f.Business.Now);
        var result = await f.Turn("Consulta sintética", "trace-failure");
        var json = JsonSerializer.SerializeToElement(trace.Turn("Consulta sintética", before, trace.Context(f.Business.Workspace, await f.Business.Context(), f.Business.Now), result, (0, 0)));
        Assert.Equal(providerError ? "ProviderError" : "InvalidJson", json.GetProperty("ModelDecisions")[0].GetProperty("Outcome").GetString());
        Assert.DoesNotContain("private-error-body", json.GetRawText()); Assert.DoesNotContain("private-invalid-body", json.GetRawText());
        Assert.Empty(f.Business.Bookings); Assert.Empty(f.Business.Orders);
    }

    [Theory]
    [InlineData("+51 912 345 678")][InlineData("912 345 678")][InlineData("51912345678")]
    public void Capture_redacts_contacts_credentials_patient_names_and_never_includes_prompts_or_free_model_text(string formattedContact)
    {
        const string secret = "synthetic-private-key"; const string phone = "+51912345678";
        var trace = new AgentEvaluationTrace(secret, phone); var state = new ConversationContextDto { RealCustomerName = "Synthetic Patient" };
        var before = trace.Context(Guid.NewGuid(), state, DateTime.UtcNow);
        var model = trace.BeginModel("{\"Phase\":\"Decision\",\"PrivatePrompt\":\"do-not-export-system\"}");
        trace.CompleteModel(model, JsonSerializer.Serialize(new AgentProposal(AgentDecision.Information, "synthetic", "do-not-export-candidate",
            Tools: [new(AgentReadKind.Services, secret + " " + formattedContact + " " + phone + " someone@example.test")])));
        var result = new AgentTurnResult(true, "Prepared", null, [], 1, 1, VerifiedText: $"{secret} {phone} Synthetic Patient PEN 75.00 2026-11-04");
        var serialized = JsonSerializer.Serialize(new { Trace = trace.Turn("Synthetic Patient " + phone, before, before, result, (0, 0)), PublicText = trace.PublicText(result) });
        foreach (var forbidden in new[] { secret, phone, formattedContact, "Synthetic Patient", "someone@example.test", "do-not-export-system", "do-not-export-candidate" }) Assert.DoesNotContain(forbidden, serialized);
        Assert.Contains("75.00", serialized); Assert.Contains("2026-11-04", serialized);
    }

    [Fact]
    public async Task Read_observer_preserves_results_and_exposes_sensitive_omissions_without_exporting_values()
    {
        var ws = Guid.NewGuid(); var trace = new AgentEvaluationTrace(); var inner = new Mock<IBusinessReadTools>(MockBehavior.Strict);
        var expected = new BusinessReadResult(ws, BusinessReadStatus.Found, "Availability", "site",
            [new("AvailableStartUtc", "2026-11-04T15:00:00Z", VerifiedFactSource.TransactionEngine), new("PriceMinorUnits", "123456789", VerifiedFactSource.BusinessRepository),
                new("Private", "do-not-export-fact", VerifiedFactSource.BusinessRepository, Sensitive: true)]);
        inner.Setup(r => r.ReadAsync(It.IsAny<BusinessReadTurn>(), It.IsAny<BusinessReadRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var observed = trace.Observe(inner.Object); var turn = new BusinessReadTools(Mock.Of<IKnowledgeService>(), Mock.Of<NexFlow.Application.Abstractions.IEntitlementService>()).Open(ws);
        Assert.Same(expected, await observed.ReadAsync(turn, new AvailabilityRead("site", "service", DateTime.Today), default));
        var snapshot = trace.Context(ws, new(), DateTime.UtcNow);
        var captured = JsonSerializer.SerializeToElement(trace.Turn("Consulta", snapshot, snapshot, AgentTurnResult.Rejected("Synthetic"), (0, 0)));
        var read = captured.GetProperty("ExecutedTools")[0]; Assert.Equal("Availability", read.GetProperty("Kind").GetString());
        Assert.Equal(1, read.GetProperty("SensitiveFactsOmitted").GetInt32()); Assert.Equal(2, read.GetProperty("Facts").GetArrayLength());
        Assert.Contains("123456789", captured.GetRawText());
        Assert.DoesNotContain("do-not-export-fact", captured.GetRawText()); inner.VerifyAll();
    }
}
