using Moq;
using Xunit;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Tests.Concierge;

public sealed class Sprint51ConversationTests
{
    [Theory]
    [InlineData("SanJeronimo")]
    [InlineData("en sanjerónimo")]
    public void Joined_words_identify_only_a_unique_authorized_entity(string input)
    {
        var result = new LocationMentionResolver().Resolve(input, [new("a", "San Jerónimo"), new("b", "Concepción")], true);
        Assert.True(result.TryGetValue(out var entity)); Assert.Equal("a", entity.Id);
        Assert.Equal(ResolutionStatus.Ambiguous, new LocationMentionResolver().Resolve(input,
            [new("a", "San Jerónimo"), new("b", "Sanjeronimo")], true).Status);
    }

    [Theory]
    [InlineData("me yamo Elena Torres", "Elena Torres")]
    [InlineData("mi nonbre es Lucas Vera", "Lucas Vera")]
    public async Task An_uncertain_presentation_reaches_semantics_and_only_its_literal_name_is_saved(string input, string name)
    {
        var f = await Draft("COLLECT_CUSTOMERNAME");
        f.Interpretations.Enqueue(new() { Intent = NexFlow.Domain.Enums.ConversationIntent.Reservation,
            CustomerName = name, Evidence = [new("Name", name, name)] });
        await f.HandleTurn(input, "name");
        Assert.Equal(name, (await f.Context()).RealCustomerName); Assert.Equal(1, f.AiCalls); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("¿Tienes para las 4pm?")]
    [InlineData("¿No se puede a las cuatro de la tarde?")]
    [InlineData("Hay algo a las 16 horas?")]
    public async Task A_point_availability_question_answers_first_without_replacing_the_draft(string input)
    {
        var f = await Draft("CONFIRM");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn(input, "availability");
        Assert.Contains("16:00", f.Sent["availability"].Content);
        Assert.Contains("disponible", f.Sent["availability"].Content);
        Assert.DoesNotContain("1.", f.Sent["availability"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task An_availability_answer_is_not_consent_and_selecting_the_hour_still_requires_confirmation()
    {
        var f = await Draft("CONFIRM");
        await f.HandleTurn("¿Y a las 4?", "ask");
        await f.HandleTurn("sí", "yes-to-question");
        Assert.Empty(f.Bookings); Assert.Equal("10:00", (await f.Context()).TargetTime);
        await f.HandleTurn("a las cuatro de la tarde", "select");
        Assert.Equal("16:00", (await f.Context()).TargetTime); Assert.Empty(f.Bookings);
        Assert.Contains("Confirmas", f.Sent["select"].Content);
        await f.HandleTurn("sí", "confirm"); await f.HandleTurn("sí", "confirm");
        Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task An_availability_interruption_does_not_block_explicit_confirmed_draft_cancellation()
    {
        var f = await Draft("CONFIRM");
        await f.HandleTurn("¿Y a las 4pm?", "question-before-cancel");
        await f.HandleTurn("cancelar", "cancel-draft");
        Assert.Equal("CANCEL", (await f.Context()).PendingRecoveryAction);
        await f.HandleTurn("sí", "confirm-draft-cancellation");
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("Nombre: Ana López\nFecha: 23/10/2026\nHora: 4 pm")]
    [InlineData("Ana López, 23/10/2026, 4pm.")]
    [InlineData("23/10/2026, 4pm, Ana López")]
    [InlineData("4pm; Ana López; 23/10/2026")]
    [InlineData("En Concepción, 23/10/2026, Ana López, 4pm")]
    [InlineData("Me llamo Ana López y quisiera una limpieza dental el 23/10/2026 a las cuatro de la tarde en Concepción.")]
    public async Task Several_slots_in_different_formats_preserve_known_entities_and_never_commit(string input)
    {
        var f = await Draft("COLLECT_DATE");
        await f.HandleTurn(input, "combined");
        var state = await f.Context();
        Assert.Equal("Ana López", state.RealCustomerName); Assert.Equal("2026-10-23", state.TargetDate);
        Assert.Equal("16:00", state.TargetTime); Assert.Equal("sede-a", state.SelectedLocationId);
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Imperfect_compound_message_uses_one_semantic_pass_with_literal_evidence()
    {
        var f = await Draft("COLLECT_DATE");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, CustomerName = "ana lopez", Date = "2026-10-21", Time = "16:00", Service = "Limpieza dental",
            Evidence = [new("Name", "ana lopez", "ana lopez"), new("Date", "mñn", "2026-10-21", "mañana"), new("Time", "a las 4 de la tarde", "16:00"), new("Service", "limpieza dental", "Limpieza dental")] });
        await f.HandleTurn("Ola soy ana lopez, qiero limpieza dental mñn a las 4 de la tarde.", "imperfect");
        var state = await f.Context();
        Assert.Equal("ana lopez", state.RealCustomerName); Assert.Equal("2026-10-21", state.TargetDate);
        Assert.Equal("16:00", state.TargetTime); Assert.Equal(1, f.AiCalls); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Semantic_location_spelling_is_backed_by_a_unique_authorized_name()
    {
        var f = await Draft("COLLECT_LOCATION");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Location = "Sede San Jeronimo",
            Evidence = [new("Location", "San Jeronimoo", "Sede San Jeronimo")] });
        await f.HandleTurn("en la sede San Jeronimoo", "site");
        Assert.Equal("sede-b", (await f.Context()).SelectedLocationId); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_stored_invalid_presentation_cannot_reach_a_confirmed_reservation()
    {
        var f = await Draft("CONFIRM"); var state = await f.Context(); state.RealCustomerName = "me yamo Elena Torres";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("sí", "invalid-name");
        Assert.Empty(f.Bookings); Assert.Null((await f.Context()).RealCustomerName);
        Assert.Contains("nombre", f.Sent["invalid-name"].Content);
    }

    [Fact]
    public async Task Information_failure_and_human_takeover_preserve_the_draft_and_suppress_delivery()
    {
        var f = await Draft("CONFIRM"); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Workspace, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unavailable"));
        await f.HandleTurn("¿Y a las 4pm?", "outage");
        Assert.Contains("No pude comprobar", f.Sent["outage"].Content); Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        f.DuringComposition = async () => { var c = await f.Context(); c.Mode = "Human"; await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, c, default); };
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.HandleTurn("¿Y a las 4pm?", "takeover"));
        Assert.False(f.Sent.ContainsKey("takeover")); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("¿Queda algún espacio por la tarde?")]
    [InlineData("¿Puedo ir mañana luego de las tres de la tarde?")]
    public async Task An_open_preference_filters_verified_times_without_inventing_a_selected_time(string input)
    {
        var f = await Draft("CONFIRM"); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn(input, "period");
        Assert.Contains("16:00", f.Sent["period"].Content); Assert.DoesNotContain("10:00", f.Sent["period"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Open_availability_has_one_list_one_question_and_no_second_engine_read_for_decoration()
    {
        var f = await Draft("COLLECT_TIME"); var state = await f.Context(); state.TargetTime = null;
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("por la tarde", "options");
        var content = f.Sent["options"].Content;
        Assert.Equal(1, content.Split("16:00").Length - 1); Assert.Equal(1, content.Count(c => c == '?'));
        f.EngineReads.Verify(e => e.GetAvailabilityAsync(f.Workspace, "sede-a", "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_service_label_is_not_a_person_and_fabricated_name_evidence_is_rejected()
    {
        var f = await Draft("COLLECT_CUSTOMERNAME");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, CustomerName = "Nombre Inventado", Evidence = [new("Name", "Nombre Inventado", "Nombre Inventado")] });
        await f.HandleTurn("me yamo Lucas", "fabricated");
        Assert.Null((await f.Context()).RealCustomerName); Assert.Empty(f.Bookings);
        var state = await f.Context(); state.RealCustomerName = "Limpieza dental"; state.CurrentStep = "CONFIRM"; state.PendingDialogueSlots.Clear();
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("sí", "service-is-not-person");
        Assert.Null((await f.Context()).RealCustomerName); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("Limpieza Profunda")]
    [InlineData("Sede San Jeronimo")]
    public async Task A_business_label_cannot_be_saved_as_the_requested_customer_name(string input)
    {
        var f = await Draft("COLLECT_CUSTOMERNAME");
        await f.HandleTurn(input, "business-label");
        Assert.Null((await f.Context()).RealCustomerName); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("san jeronimo")]
    [InlineData("limpieza profunda")]
    public async Task Semantic_name_evidence_cannot_relabel_an_authorized_business_entity_as_a_person(string input)
    {
        var f = await Draft("COLLECT_CUSTOMERNAME");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, CustomerName = input, Evidence = [new("Name", input, input)] });
        await f.HandleTurn(input, "semantic-label");
        Assert.Null((await f.Context()).RealCustomerName); Assert.Empty(f.Bookings);
    }

    private static async Task<AssistantConversationTests.Fixture> Draft(string step)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true) { Now = new(2026,10,20,21,0,0,DateTimeKind.Utc) };
        await f.SeedDraft("RESERVATION"); var state = await f.Context();
        state.CurrentStep = step; state.TargetDate = "2026-10-22"; state.TargetTime = "10:00";
        state.RealCustomerName = step == "COLLECT_CUSTOMERNAME" ? null : "Persona Autorizada";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        return f;
    }
}
