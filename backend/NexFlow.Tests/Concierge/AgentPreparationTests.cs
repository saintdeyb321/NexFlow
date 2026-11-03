using System.Text.Json;
using Moq;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentPreparationTests
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
}
