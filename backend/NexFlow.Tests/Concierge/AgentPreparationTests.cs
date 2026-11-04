using System.Text.Json;
using Moq;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class AgentPreparationTests(ITestOutputHelper output)
{
    private static AgentConversationPlan Booking(AgentBookingPatch patch, AgentSpeechAct speech = AgentSpeechAct.Resume)
        => new(AgentCommunicativeGoal.BookingPreparation, speech, Booking: patch);
    private static AgentSlotCandidate C(string value, string evidence) => new(value, evidence);

    [Theory]
    [InlineData("Evaluación odontológica")]
    [InlineData("Asesoría de corte")]
    public async Task Semantic_multiple_fields_are_validated_and_staged_without_commercial_effects(string service)
    {
        var f = new AgentDialogueFixture(); f.Business.Services[0].Name = service;
        var text = $"Quiero {service} en Concepción mañana por la tarde, soy Ana Ramos";
        f.Plan(text, AgentDecision.PrepareReservation, conversation: Booking(new(Location: C("sede-a", "Concepción"),
            Service: C("service-a", service), Date: C("2026-11-04", "mañana"), Time: C("Period", "por la tarde"), Name: C("Ana Ramos", "Ana Ramos"))));
        var reply = await f.Turn(text, "compound"); var state = await f.Business.Context();
        Assert.True(reply.Publishable, reply.Outcome); Assert.Equal("sede-a", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("2026-11-04", state.TargetDate); Assert.Equal("Ana Ramos", state.RealCustomerName);
        Assert.Null(state.TargetTime); Assert.Equal(TimePreferenceKind.Period, state.PreferredTime!.Kind);
        Assert.Equal("COLLECT_TIME", state.CurrentStep); NoEffects(f);
    }

    [Fact]
    public async Task Editing_location_preserves_known_service_date_and_patient_but_rechecks_time()
    {
        var f = new AgentDialogueFixture(); await f.Business.SeedDraft("RESERVATION");
        f.Business.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Business.Workspace, "sede-b", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("synthetic unavailable read"));
        var before = await f.Business.Context(); before.TargetDate = "2026-11-04"; before.TargetTime = "10:00";
        await f.Business.Contexts.SaveContextAsync(f.Business.Workspace, AssistantConversationTests.Fixture.Phone, before, default);
        const string text = "Mejor atenderme en San Jerónimo";
        f.Plan(text, AgentDecision.PrepareReservation, conversation: Booking(new(Location: C("sede-b", "San Jerónimo"))));
        await f.Turn(text, "edit-site"); var state = await f.Business.Context();
        Assert.Equal("sede-b", state.SelectedLocationId); Assert.Equal(before.SelectedServiceId, state.SelectedServiceId);
        Assert.Equal(before.RealCustomerName, state.RealCustomerName); Assert.Equal(before.TargetDate, state.TargetDate);
        Assert.Null(state.TargetTime); Assert.Equal(TimePreferenceKind.Exact, state.PreferredTime!.Kind); // engine not configured to accept it
        NoEffects(f);
    }

    [Fact]
    public async Task Informational_intent_cannot_apply_embedded_slot_candidates_or_erase_preparation()
    {
        var f = new AgentDialogueFixture(); await f.Business.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Business.Context());
        const string text = "¿Mañana por la tarde abren?";
        f.Plan(text, AgentDecision.Information, conversation: new(AgentCommunicativeGoal.Information, AgentSpeechAct.Explain,
            Booking: new(Date: C("2026-11-04", "Mañana"), Time: C("Period", "por la tarde"))));
        await f.Turn(text, "information"); Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Business.Context())); NoEffects(f);
    }

    [Fact]
    public async Task Ambiguous_or_invented_entity_never_changes_the_draft()
    {
        var f = new AgentDialogueFixture(); await f.Business.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Business.Context());
        const string text = "La otra sede";
        f.Plan(text, AgentDecision.PrepareReservation, conversation: Booking(new(Location: C("invented-site", "otra sede"))));
        var reply = await f.Turn(text, "ambiguous");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Business.Context()));
        Assert.Contains("sede", reply.VerifiedText!); NoEffects(f);
    }

    [Fact]
    public async Task Conversational_style_varies_by_speech_act_without_changing_verified_prices()
    {
        var f = new AgentDialogueFixture();
        f.Plan("¿Cuánto cuesta?", AgentDecision.Information, AgentReadKind.Services,
            conversation: new(AgentCommunicativeGoal.Information, AgentSpeechAct.Orient));
        var first = await f.Turn("¿Cuánto cuesta?", "orient"); f.Business.Now = f.Business.Now.AddMinutes(1);
        f.Plan("No entendí, explícamelo por favor", AgentDecision.Information, AgentReadKind.Services,
            conversation: new(AgentCommunicativeGoal.Information, AgentSpeechAct.Repair));
        var repaired = await f.Turn("No entendí, explícamelo por favor", "repair");
        Assert.NotEqual(first.VerifiedText, repaired.VerifiedText); Assert.Contains("60.00", first.VerifiedText!); Assert.Contains("60.00", repaired.VerifiedText!);
        Assert.Empty(repaired.Proposal!.CandidateResponse); NoEffects(f);
    }

    private static void NoEffects(AgentDialogueFixture f) { Assert.Empty(f.Business.Bookings); Assert.Empty(f.Business.Orders); Assert.Empty(f.Business.Sent); }

    [Fact]
    public async Task Exact_time_is_kept_only_when_the_authorized_engine_returns_that_instant()
    {
        var f = new AgentDialogueFixture(); await f.Business.SeedDraft("RESERVATION");
        var initial = await f.Business.Context(); initial.TargetDate = "2026-11-04";
        await f.Business.Contexts.SaveContextAsync(f.Business.Workspace, AssistantConversationTests.Fixture.Phone, initial, default);
        const string text = "Prefiero a las 10:00";
        f.Plan(text, AgentDecision.PrepareReservation, conversation: Booking(new(Time: C("10:00", "a las 10:00"))));
        var reply = await f.Turn(text, "exact");
        Assert.Equal("10:00", (await f.Business.Context()).TargetTime); Assert.Contains("aparece disponible en esta consulta", reply.VerifiedText!); NoEffects(f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Numeric_anaphora_is_bound_to_current_options_and_expiry(bool expired)
    {
        var f = new AgentDialogueFixture();
        f.Plan("Muéstrame las atenciones", AgentDecision.Information, AgentReadKind.Services,
            conversation: new(AgentCommunicativeGoal.Information, AgentSpeechAct.Orient));
        await f.Turn("Muéstrame las atenciones", "options");
        var question = (await f.Business.Context()).CurrentQuestion!;
        if (expired) f.Business.Now = f.Business.Now.AddMinutes(11);
        const string text = "La segunda para mi visita";
        f.Plan(text, AgentDecision.PrepareReservation, conversation: Booking(new(Service: new("service-b", "La segunda", 2, question.QuestionId))));
        await f.Turn(text, "reference"); var state = await f.Business.Context();
        if (expired) { Assert.Null(state.CurrentGoal); Assert.Empty(state.PendingServiceIds); }
        else { Assert.Equal("service-b", Assert.Single(state.PendingServiceIds)); Assert.Equal("COLLECT_LOCATION", state.CurrentStep); }
        NoEffects(f);
    }

    [Fact]
    public async Task Factual_selection_exposes_only_the_entities_actually_presented()
    {
        var f = new AgentDialogueFixture();
        f.Plan("Cuéntame la otra atención", AgentDecision.Information, AgentReadKind.Services,
            conversation: new(AgentCommunicativeGoal.Information, AgentSpeechAct.Orient, References: [new(0, "service-b")]));
        var reply = await f.Turn("Cuéntame la otra atención", "filtered-facts"); var state = await f.Business.Context();
        Assert.Contains("Extracción dental", reply.VerifiedText!); Assert.DoesNotContain("Evaluación odontológica", reply.VerifiedText!);
        Assert.Equal("service-b", Assert.Single(state.RecoveryOptions).Key); NoEffects(f);
    }

    [Fact]
    public async Task Ten_turn_preparation_preserves_partial_edits_and_side_questions_until_bound_discard()
    {
        var f = new AgentDialogueFixture();
        (string Text, AgentDecision Act, AgentReadKind? Read, AgentConversationPlan? Plan)[] turns = [
            ("Quisiera saber qué atenciones hay", AgentDecision.Information, AgentReadKind.Services, null),
            ("Evaluación odontológica en Concepción mañana por la tarde, soy Ana Ramos", AgentDecision.PrepareReservation, null,
                Booking(new(Location: C("sede-a", "Concepción"), Service: C("service-a", "Evaluación odontológica"),
                    Date: C("2026-11-04", "mañana"), Time: C("Period", "por la tarde"), Name: C("Ana Ramos", "Ana Ramos")))),
            ("Antes, cuéntame del negocio", AgentDecision.Information, AgentReadKind.Profile, null),
            ("Me atenderé en San Jerónimo", AgentDecision.PrepareReservation, null, Booking(new(Location: C("sede-b", "San Jerónimo")))),
            ("Me acomoda a las 10:00", AgentDecision.PrepareReservation, null, Booking(new(Time: C("10:00", "a las 10:00")))),
            ("Corrige el paciente a Luis Salas", AgentDecision.PrepareReservation, null, Booking(new(Name: C("Luis Salas", "Luis Salas")))),
            ("¿Y qué otras atenciones ofrecen?", AgentDecision.Information, AgentReadKind.Services, null),
            ("Quiero cancelar la reserva", AgentDecision.ClarifyCancellationScope, null, null),
            ("La que estamos preparando", AgentDecision.ProposeDiscardDraft, null, null),
            ("Sí", AgentDecision.ProposeDiscardDraft, null, null) ];
        for (var i = 0; i < turns.Length; i++) {
            f.Business.Now = f.Business.Now.AddMinutes(2);
            var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Business.Context());
            var (text, decision, read, plan) = turns[i]; f.Plan(text, decision, read, conversation: plan);
            var reply = await f.Turn(text, "walkthrough-" + i); var state = await f.Business.Context();
            Assert.True(reply.Publishable, reply.Outcome); Assert.False(string.IsNullOrWhiteSpace(reply.VerifiedText));
            if (i is 0 or 2 or 6) Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(state));
            if (i is >= 1 and <= 8) {
                Assert.Equal("service-a", state.SelectedServiceId); Assert.Equal("2026-11-04", state.TargetDate);
                Assert.Equal(i >= 3 ? "sede-b" : "sede-a", state.SelectedLocationId);
                Assert.Equal(i >= 5 ? "Luis Salas" : "Ana Ramos", state.RealCustomerName);
            }
            if (i is 1 or 3) Assert.Null(state.TargetTime);
            if (i is >= 4 and <= 8) Assert.Equal("10:00", state.TargetTime);
            if (i == 8) Assert.Equal("AGENT_CANCEL_CONFIRM", state.PendingRecoveryAction);
            if (i == 9) { Assert.Null(state.CurrentGoal); Assert.Null(state.RealCustomerName); Assert.Null(state.TargetDate); }
            NoEffects(f);
            output.WriteLine($"Turn {i + 1}; USER: {text}\nASSISTANT: {reply.VerifiedText}\n" +
                $"STATE: goal={state.CurrentGoal}, location={state.SelectedLocationId}, service={state.SelectedServiceId}, date={state.TargetDate}, time={state.TargetTime}, step={state.CurrentStep}; models={reply.ModelCalls}; tools={reply.ToolCalls}");
        }
        Assert.Equal(0, f.Business.AiCalls);
    }
}
