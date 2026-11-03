using System.Text.Json;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class AgentDialogueTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Eight_turn_owner_circuit_reads_facts_preserves_interruptions_and_discards_only_after_bound_consent()
    {
        var f = new AgentDialogueFixture();
        (string Text, AgentDecision Act, AgentReadKind? Read)[] turns = [
            ("Hola quiero sacarme el diente", AgentDecision.Information, AgentReadKind.Services),
            ("Quiero extraerme el diente", AgentDecision.Information, AgentReadKind.Services),
            ("Quiero reservar", AgentDecision.PrepareReservation, null),
            ("Quiero hacerme una consulta", AgentDecision.Information, AgentReadKind.Services),
            ("Quiero cancelar la reserva", AgentDecision.ClarifyCancellationScope, null),
            ("A la que estamos preparando", AgentDecision.ProposeDiscardDraft, null),
            ("Sí", AgentDecision.ProposeDiscardDraft, null),
            ("¿Qué productos tienen?", AgentDecision.Information, AgentReadKind.Products) ];
        for (var i = 0; i < turns.Length; i++) {
            f.Business.Now = f.Business.Now.AddMinutes(1);
            var (text, act, read) = turns[i]; var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Business.Context());
            f.Plan(text, act, read); var reply = await f.Turn(text, "owner-" + i);
            Assert.True(reply.Publishable, reply.Outcome); Assert.False(string.IsNullOrWhiteSpace(reply.VerifiedText));
            var state = await f.Business.Context();
            if (i < 2 || i == 3 || i == 7) Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(state));
            if (i == 2) Assert.Equal("RESERVATION", state.CurrentGoal);
            if (i == 5) { Assert.Equal("AGENT_CANCEL_CONFIRM", state.PendingRecoveryAction); Assert.Equal("RESERVATION", state.CurrentGoal); }
            if (i == 6) { Assert.Null(state.CurrentGoal); Assert.Null(state.RealCustomerName); }
            Assert.Empty(f.Business.Bookings); Assert.Empty(f.Business.Orders); Assert.Empty(f.Business.Sent);
            Assert.DoesNotContain("Propuesta del modelo", reply.VerifiedText!);
            output.WriteLine($"{i}: {text}\n{reply.VerifiedText}\nstate={state.CurrentGoal}/{state.PendingRecoveryAction}; calls={reply.ModelCalls}");
        }
        Assert.Equal(0, f.Business.AiCalls); Assert.Empty(f.Business.Plans);
    }

    [Fact]
    public async Task Prepared_result_survives_replay_and_recreated_runtime_without_another_model_call()
    {
        var f = new AgentDialogueFixture(); const string text = "¿Qué servicios tienen?";
        f.Plan(text, AgentDecision.Information, AgentReadKind.Services);
        var first = await f.Turn(text, "recover"); var inputs = f.Inputs.Count;
        var replay = await f.Turn(text, "recover");
        Assert.True(first.Publishable, first.Outcome); Assert.True(replay.Publishable, replay.Outcome); Assert.True(replay.Recovered);
        Assert.Equal(first.VerifiedText, replay.VerifiedText); Assert.Equal(0, replay.ModelCalls); Assert.Equal(inputs, f.Inputs.Count);
        Assert.Empty(f.Business.Sent); Assert.Empty(f.Business.Bookings);
    }

    [Fact]
    public async Task Started_orphan_has_observable_failure_and_never_restarts_the_model()
    {
        var f = new AgentDialogueFixture(); var request = f.Request("Consulta nueva", "orphan");
        var claim = await f.Journal.BeginAsync(request, await f.Business.Context(), f.Options, default);
        Assert.NotNull(claim.Lease);
        using var scope = AgentExperimentScope.Begin(f.Business.Workspace);
        var pending = await f.Runtime.HandleAsync(request, default);
        Assert.Equal("InProgress", pending.Outcome); Assert.False(pending.Publishable); Assert.NotEmpty(pending.StatusExplanation!);
        f.Business.Now = f.Business.Now.AddSeconds(31);
        var recovered = await f.Runtime.HandleAsync(request, default);
        Assert.Equal("Abandoned", recovered.Outcome); Assert.True(recovered.Publishable); Assert.Equal(0, recovered.ModelCalls); Assert.Empty(f.Inputs);
    }

    [Fact]
    public async Task Flag_or_harness_scope_off_keeps_legacy_and_cannot_enable_real_traffic()
    {
        var f = new AgentDialogueFixture();
        await f.Turn("menú", "legacy", scope: false);
        Assert.Empty(f.Inputs); Assert.NotEmpty(f.Business.Sent);
        f.Options.Enabled = false;
        using var scope = AgentExperimentScope.Begin(f.Business.Workspace);
        Assert.False(f.Runtime.Handles(f.Business.Workspace));
        var denied = await f.Runtime.HandleAsync(f.Request("Otra consulta", "off"), default);
        Assert.False(denied.Publishable); Assert.Equal("DialogueDisabled", denied.Outcome);
    }

    [Fact]
    public async Task Preparation_question_metadata_matches_the_question_actually_asked()
    {
        var f = new AgentDialogueFixture(); f.Plan("Quiero reservar", AgentDecision.PrepareReservation);
        var answer = await f.Turn("Quiero reservar", "question"); var state = await f.Business.Context();
        Assert.Contains("servicio", answer.VerifiedText!);
        Assert.Contains("servicio", state.CurrentQuestion!.LinguisticQuestion(state));
        Assert.Equal("COLLECT_SERVICE", state.CurrentStep);
    }

    [Fact]
    public async Task Resuming_preparation_does_not_repeat_known_service_or_lose_the_next_missing_field()
    {
        var f = new AgentDialogueFixture(); await f.Business.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Business.Context());
        f.Plan("Sigamos con la preparación", AgentDecision.PrepareReservation);
        var result = await f.Turn("Sigamos con la preparación", "resume"); var after = await f.Business.Context();
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(after));
        Assert.Contains("fecha", result.VerifiedText!); Assert.Equal("COLLECT_DATE", after.CurrentStep);
        Assert.Empty(f.Business.Bookings); Assert.Empty(f.Business.Orders); Assert.Empty(f.Business.Sent);
    }
}
