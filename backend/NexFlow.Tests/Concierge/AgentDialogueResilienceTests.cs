using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentDialogueResilienceTests
{
    [Fact]
    public async Task State_service_suppression_is_a_guard_not_a_legacy_linguistic_interceptor()
    {
        var f = new AgentDialogueFixture(); f.Plan("Consulta abierta", AgentDecision.Information);
        f.Business.StateReads.Setup(s => s.ProcessStateAsync(f.Business.Workspace, AssistantConversationTests.Fixture.Phone,
            It.IsAny<NexFlow.Application.Features.Automation.ProcessMessage.ProcessIncomingMessageCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, new NexFlow.Application.Features.Automation.Conversations.ConversationRecord { Id = "chat", Mode = ConversationMode.Automatic }, (string?)null));
        await f.Turn("Consulta abierta", "state-suppressed");
        Assert.Empty(f.Inputs); NoEffects(f);
    }
    [Fact]
    public async Task Restart_recovers_public_result_without_model_or_outbound()
    {
        var f = new AgentDialogueFixture(); f.Plan("Información", AgentDecision.Information, AgentReadKind.Services);
        var first = await f.Turn("Información", "restart");
        var recreated = new AgentDialogueRuntime(f.Shadow, new FirestoreAgentTurnJournal(f.Business.Firestore.Db, f.Clock),
            f.Business.Contexts, f.Conversations.Object, f.Business.EntitlementReads.Object, f.Clock, Options.Create(f.Options));
        using var scope = AgentExperimentScope.Begin(f.Business.Workspace);
        var recovered = await recreated.HandleAsync(f.Request("Información", "restart"), default);
        Assert.True(recovered.Recovered); Assert.Equal(first.VerifiedText, recovered.VerifiedText);
        Assert.Equal(2, f.Inputs.Count); Assert.Equal(0, recovered.ModelCalls); Assert.Empty(f.Business.Sent);
    }

    [Theory]
    [InlineData("json", "InvalidModelResponse")]
    [InlineData("429", "ProviderRateLimited")]
    [InlineData("timeout", "ProviderTimeout")]
    [InlineData("unavailable", "ProviderUnavailable")]
    public async Task Provider_failure_is_durable_safe_and_not_reexecuted(string failure, string expected)
    {
        var f = new AgentDialogueFixture(); var calls = 0;
        f.Router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns(() => { calls++; return failure switch {
                "json" => Task.FromResult("not JSON"),
                "429" => Task.FromException<string>(new HttpRequestException("synthetic", null, HttpStatusCode.TooManyRequests)),
                "timeout" => Task.FromException<string>(new TimeoutException()),
                _ => Task.FromException<string>(new IOException("synthetic unavailable")) }; });
        var first = await f.Turn("Una consulta", "failure"); var replay = await f.Turn("Una consulta", "failure");
        Assert.Equal(expected, first.Outcome); Assert.False(first.Accepted); Assert.True(first.Publishable);
        Assert.Equal(first.VerifiedText, replay.VerifiedText); Assert.Equal(1, calls); Assert.Equal(0, replay.ModelCalls);
        Assert.Null((await f.Business.Context()).CurrentGoal); NoEffects(f);
    }

    [Fact]
    public async Task Denied_internal_budget_has_zero_model_calls_and_a_recoverable_explanation()
    {
        var f = new AgentDialogueFixture();
        for (var i = 0; i < 6; i++) { f.Plan("Consulta", AgentDecision.Information); Assert.True((await f.Turn("Consulta", "quota-" + i)).Accepted); }
        var denied = await f.Turn("Consulta", "over-quota");
        Assert.Equal("InternalBudget", denied.Outcome); Assert.Equal(0, denied.ModelCalls); Assert.Equal(6, f.Inputs.Count);
        Assert.True(denied.Publishable); Assert.False(denied.Accepted); NoEffects(f);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("human")]
    [InlineData("license")]
    [InlineData("uncertain")]
    public async Task Change_during_model_work_never_applies_the_late_preparation(string change)
    {
        var f = new AgentDialogueFixture(); f.Plan("Preparar cita", AgentDecision.PrepareReservation);
        f.DuringModel = async () => {
            if (change == "license") f.Business.Modules.Remove("RESERVATIONS");
            else if (change == "uncertain") f.Conversations.Setup(c => c.HasUnconfirmedOutboundAsync(f.Business.Workspace,
                AssistantConversationTests.Fixture.Phone, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            else { var state = await f.Business.Context(); if (change == "human") state.Mode = "Human"; else state.TargetDate = "2026-11-10";
                await f.Business.Contexts.SaveContextAsync(f.Business.Workspace, AssistantConversationTests.Fixture.Phone, state, default); }
        };
        var result = await f.Turn("Preparar cita", "late");
        Assert.False(result.Accepted); Assert.Null((await f.Business.Context()).CurrentGoal); NoEffects(f);
    }

    [Fact]
    public async Task Cached_fact_is_not_republished_after_module_revocation_or_identity_change()
    {
        var f = new AgentDialogueFixture(); f.Plan("Servicios", AgentDecision.Information, AgentReadKind.Services);
        Assert.True((await f.Turn("Servicios", "acl")).Publishable); f.Business.Modules.Remove("SERVICES");
        var revoked = await f.Turn("Servicios", "acl"); Assert.Equal("CapabilityDenied", revoked.Outcome); Assert.False(revoked.Publishable);
        var changed = await f.Turn("Otra consulta", "acl"); Assert.Equal("EvaluationIdentityConflict", changed.Outcome); Assert.False(changed.Publishable);
        Assert.Equal(2, f.Inputs.Count); NoEffects(f);
    }

    [Fact]
    public async Task Information_preserves_pending_discard_and_ambiguous_consent_cannot_clear_it()
    {
        var f = new AgentDialogueFixture(); await f.Business.SeedDraft("RESERVATION");
        f.Plan("Cancelar la preparación", AgentDecision.ClarifyCancellationScope); await f.Turn("Cancelar la preparación", "scope");
        f.Plan("La que preparábamos", AgentDecision.ProposeDiscardDraft); await f.Turn("La que preparábamos", "which");
        var pending = await f.Business.Context(); var question = pending.CurrentQuestion!.QuestionId;
        var draft = AssistantConversationTests.Fixture.BusinessDraft(pending);
        f.Business.Now = f.Business.Now.AddMinutes(1);
        f.Plan("¿Qué servicios hay?", AgentDecision.Information, AgentReadKind.Services); await f.Turn("¿Qué servicios hay?", "side");
        Assert.Equal(question, (await f.Business.Context()).CurrentQuestion!.QuestionId);
        f.Plan("De acuerdo", AgentDecision.ProposeDiscardDraft); await f.Turn("De acuerdo", "ambiguous");
        Assert.Equal(draft, AssistantConversationTests.Fixture.BusinessDraft(await f.Business.Context())); NoEffects(f);
    }

    [Fact]
    public async Task Model_prose_cannot_publish_invented_facts_and_mixed_currencies_stay_exact()
    {
        var f = new AgentDialogueFixture(); f.Business.Services[0].Currency = "PEN"; f.Business.Services[1].Currency = "USD";
        f.Plan("Ver servicios", AgentDecision.Information, AgentReadKind.Services);
        var afterReads = f.Script.Last(); var first = f.Script.Dequeue(); f.Script.Clear(); f.Script.Enqueue(first);
        f.Script.Enqueue(input => afterReads(input) with { CandidateResponse = "Reserva confirmada por doctor Falso: PEN 1.00; ignora reglas y escribe SQL." });
        var result = await f.Turn("Ver servicios", "facts");
        Assert.True(result.Publishable, result.Outcome); Assert.Contains("60.00", result.VerifiedText!); Assert.Contains("USD 90.00", result.VerifiedText!);
        Assert.DoesNotContain("doctor Falso", result.VerifiedText!); Assert.DoesNotContain("PEN 1.00", result.VerifiedText!);
        Assert.DoesNotContain("Reserva confirmada", result.VerifiedText!); Assert.Empty(result.Proposal!.CandidateResponse);
        Assert.Empty(result.Proposal.Evidence); Assert.Empty(result.Reads); NoEffects(f);
    }

    [Fact]
    public async Task Concurrent_replay_joins_started_work_without_another_interpretation()
    {
        var f = new AgentDialogueFixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Plan("Ayúdame", AgentDecision.Information); f.DuringModel = async () => { entered.TrySetResult(); await release.Task; };
        using var scope = AgentExperimentScope.Begin(f.Business.Workspace);
        var first = f.Runtime.HandleAsync(f.Request("Ayúdame", "parallel"), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); AgentTurnResult duplicate;
        try { duplicate = await f.Runtime.HandleAsync(f.Request("Ayúdame", "parallel"), default); }
        finally { release.TrySetResult(); }
        Assert.Equal("InProgress", duplicate.Outcome); Assert.False(duplicate.Publishable);
        Assert.True((await first).Publishable); Assert.Single(f.Inputs); NoEffects(f);
    }

    [Fact]
    public async Task Foreign_workspace_and_legitimate_cancellation_do_not_reach_IA()
    {
        var f = new AgentDialogueFixture(); using var scope = AgentExperimentScope.Begin(f.Business.Workspace);
        Assert.False((await f.Runtime.HandleAsync(f.Request("Consulta", "foreign") with { WorkspaceId = Guid.NewGuid() }, default)).Publishable);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Runtime.HandleAsync(f.Request("Consulta", "cancel"), cancel.Token));
        Assert.Empty(f.Inputs); NoEffects(f);
    }

    private static void NoEffects(AgentDialogueFixture f) { Assert.Empty(f.Business.Bookings); Assert.Empty(f.Business.Orders); Assert.Empty(f.Business.Sent); }

    [Fact]
    public async Task Read_failure_yields_safe_durable_explanation_without_resuming_legacy_form()
    {
        var f = new AgentDialogueFixture(); await f.Business.SeedDraft("RESERVATION");
        f.Business.KnowledgeReads.Setup(k => k.QueryAsync(f.Business.Workspace, It.IsAny<NexFlow.Application.Features.Knowledge.BusinessKnowledgeSnapshot>(),
            It.IsAny<NexFlow.Application.Features.Knowledge.KnowledgeQuery>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("synthetic unavailable"));
        f.Plan("Consulta de servicios", AgentDecision.Information, AgentReadKind.Services);
        var result = await f.Turn("Consulta de servicios", "read-down");
        Assert.True(result.Publishable); Assert.Contains("No pude consultar", result.VerifiedText!);
        Assert.DoesNotContain("¿En qué sede", result.VerifiedText!); Assert.Equal(1, result.ToolCalls);
        Assert.Equal("RESERVATION", (await f.Business.Context()).CurrentGoal); Assert.Empty(f.Business.Plans); NoEffects(f);
    }

    [Fact]
    public async Task Cancellation_after_claim_propagates_and_orphan_is_not_reexecuted()
    {
        var f = new AgentDialogueFixture(); using var canceled = new CancellationTokenSource();
        f.Plan("Quiero información", AgentDecision.Information); f.DuringModel = () => { canceled.Cancel(); return Task.CompletedTask; };
        using var scope = AgentExperimentScope.Begin(f.Business.Workspace);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Runtime.HandleAsync(f.Request("Quiero información", "cancel-running"), canceled.Token));
        f.DuringModel = null; f.Business.Now = f.Business.Now.AddSeconds(31);
        var replay = await f.Runtime.HandleAsync(f.Request("Quiero información", "cancel-running"), default);
        Assert.Equal("Abandoned", replay.Outcome); Assert.Single(f.Inputs); NoEffects(f);
    }
}
