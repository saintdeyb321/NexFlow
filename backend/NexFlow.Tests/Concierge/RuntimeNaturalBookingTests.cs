using Moq;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class RuntimeNaturalBookingTests
{
    private static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true) { Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public async Task Time_question_accepts_a_preference_without_fetching_or_listing_grid_slots()
    {
        var f = Create();
        await f.HandleTurn("Soy Ana Pérez, quiero limpieza dental en Concepción pasado mañana por la tarde", "start");
        var state = await f.Context(); Assert.Equal("COLLECT_TIME", state.CurrentStep); Assert.Null(state.TargetTime);
        Assert.Empty(state.RecoveryOptions); Assert.Contains("hora", f.Sent["start"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("16:00", f.Sent["start"].Content); Assert.DoesNotContain("Horarios disponibles", f.Sent["start"].Content);
        f.EngineReads.Verify(e => e.GetAvailabilityAsync(f.Workspace, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData("11 am", "11:00")]
    [InlineData("a las cuatro de la tarde", "16:00")]
    [InlineData("16:30", "16:30")]
    [InlineData("10:20", "10:20")]
    public async Task Exact_times_including_free_starts_are_verified_at_the_requested_instant(string text, string expected)
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var state = await f.Context();
        state.TargetDate = "2026-10-22"; state.CurrentStep = "COLLECT_TIME";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        f.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Workspace, "sede-a", "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, string _, DateTime local, CancellationToken _) => new[] {
                new TimeSlotDto(DateTime.SpecifyKind(local.AddHours(5), DateTimeKind.Utc), DateTime.SpecifyKind(local.AddHours(5.5), DateTimeKind.Utc), true) });
        await f.HandleTurn(text, "time");
        Assert.Equal(expected, (await f.Context()).TargetTime); Assert.Empty(f.Bookings);
        Assert.Equal(ExpectedReply.ConfirmReservation, (await f.Context()).CurrentQuestion!.Expected);
        f.EngineReads.Verify(e => e.GetAvailabilityAtAsync(f.Workspace, "sede-a", "service-a",
            It.Is<DateTime>(d => d.ToString("HH:mm") == expected), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Lowercase_name_is_literal_zero_token_data_and_commit_still_requires_current_summary()
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var state = await f.Context();
        state.TargetDate = "2026-10-22"; state.TargetTime = "16:00"; state.RealCustomerName = null; state.CurrentStep = "COLLECT_CUSTOMERNAME";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("zaira nube", "name");
        Assert.Equal("zaira nube", (await f.Context()).RealCustomerName); Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
        await f.HandleTurn("sí confirmo", "consent"); await f.HandleTurn("sí confirmo", "consent");
        Assert.Equal("zaira nube", Assert.Single(f.Bookings.Values).CustomerName);
    }

    [Fact]
    public async Task Options_remain_available_on_explicit_request_and_a_period_does_not_select_an_hour()
    {
        var f = Create(); await f.HandleTurn("Soy Ana Pérez, quiero limpieza dental en Concepción pasado mañana por la tarde", "start");
        await f.HandleTurn("muéstrame opciones", "options");
        Assert.Contains("16:00", f.Sent["options"].Content); Assert.DoesNotContain("10:00", f.Sent["options"].Content);
        Assert.Null((await f.Context()).TargetTime); Assert.Empty(f.Bookings);
        await f.HandleTurn("1", "select"); Assert.Equal("16:00", (await f.Context()).TargetTime);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Availability_reference_selects_only_a_verified_current_time_and_never_confirms_it(bool occupiedAfterQuery)
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var state = await f.Context();
        state.TargetDate = "2026-10-22"; state.CurrentStep = "COLLECT_TIME";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("¿Hay a las cuatro de la tarde?", "query");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Bookings);
        if (occupiedAfterQuery) f.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Workspace, "sede-a", "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TimeSlotDto>());
        f.Interpretations.Enqueue(new() { Intent = NexFlow.Domain.Enums.ConversationIntent.Reservation,
            Directive = ConversationDirective.SelectReferencedTime, Act = DialogueAct.Correction,
            Evidence = [new("Act", "quiero esa hora", nameof(DialogueAct.Correction))] });
        await f.HandleTurn("quiero esa hora", "select-reference");
        Assert.Empty(f.Bookings);
        Assert.Equal(occupiedAfterQuery ? null : "16:00", (await f.Context()).TargetTime);
        if (!occupiedAfterQuery)
        {
            await f.HandleTurn("sí confirmo", "confirm-reference");
            Assert.Single(f.Bookings);
        }
    }

    [Theory]
    [InlineData("lo que vimos antes")]
    [InlineData("eso de la otra vez")]
    [InlineData("para el jueves")]
    public void Referential_prose_is_not_mistaken_for_an_unequivocal_lowercase_name(string text)
    {
        var resolved = new NexFlow.Application.Features.Automation.Dialogue.Understanding.NameSlotResolver().Resolve(text, true);
        Assert.NotEqual(ResolutionStatus.Determined, resolved.Status);
    }

    [Fact]
    public void Unrequested_compound_prose_requires_semantics_instead_of_overwriting_the_customer_name()
    {
        var resolved = new NexFlow.Application.Features.Automation.Dialogue.Understanding.NameSlotResolver()
            .Resolve("revisemos eso, continúa tranquilo", false, compoundAnswer: true);
        Assert.NotEqual(ResolutionStatus.Determined, resolved.Status);
    }

    [Theory]
    [InlineData(false, "10:00")]
    [InlineData(true, "10:00")]
    [InlineData(true, "quiero esa hora")]
    public async Task Mvp_reference_without_draft_date_survives_ack_and_selects_only_before_separate_consent(bool acknowledge, string selection)
    {
        var f = Create(); f.Now = new(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);
        await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("¿Está disponible pasado mañana a las 10?", "query-mvp");
        var queried = await f.Context();
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(queried));
        var reference = Assert.IsType<ReservationTimeReference>(ReservationTimeReference.Current(f.Workspace, queried, f.Now));
        Assert.Equal("2026-10-28", reference.Date); Assert.Equal("10:00", reference.Time); Assert.Empty(f.Bookings);
        if (acknowledge)
        {
            await f.HandleTurn("vale", "ack-mvp");
            var acknowledged = await f.Context();
            Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(acknowledged));
            Assert.Equal(reference, ReservationTimeReference.Current(f.Workspace, acknowledged, f.Now));
            Assert.Contains("28/10/2026", f.Sent["ack-mvp"].Content);
            Assert.Contains("10:00", f.Sent["ack-mvp"].Content); Assert.Equal(0, f.AiCalls);
        }
        if (selection == "quiero esa hora") f.Interpretations.Enqueue(new() {
            Intent = ConversationIntent.Reservation, Directive = ConversationDirective.SelectReferencedTime,
            Act = DialogueAct.Correction, Evidence = [new("Act", selection, nameof(DialogueAct.Correction))] });
        await f.HandleTurn(selection, "select-mvp");
        var selected = await f.Context();
        Assert.Equal("2026-10-28", selected.TargetDate); Assert.Equal("10:00", selected.TargetTime);
        Assert.Equal(ExpectedReply.ConfirmReservation, selected.CurrentQuestion?.Expected); Assert.Empty(f.Bookings);
        await f.HandleTurn("vale", "not-consent-mvp"); Assert.Empty(f.Bookings);
        await f.HandleTurn("sí confirmo", "consent-mvp"); await f.HandleTurn("sí confirmo", "consent-mvp");
        var created = Assert.Single(f.Bookings.Values); Assert.Equal(new DateTime(2026, 10, 28, 15, 0, 0, DateTimeKind.Utc), created.StartTime);
        f.EngineReads.Verify(e => e.CreateReservationAsync(f.Workspace, "sede-a", "service-a", AssistantConversationTests.Fixture.Phone,
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), "consent-mvp"), Times.Once);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("workspace")]
    [InlineData("changed-site")]
    [InlineData("occupied")]
    public async Task Mvp_reference_never_selects_an_obsolete_foreign_or_occupied_slot(string reason)
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        await f.HandleTurn("¿Hay pasado mañana a las 10:00?", "query-guard");
        var state = await f.Context(); Assert.NotNull(state.Information?.AvailabilityReference);
        if (reason == "expired") f.Now = f.Now.AddMinutes(11);
        if (reason == "workspace") {
            state.Information!.AvailabilityReference = state.Information.AvailabilityReference! with { WorkspaceId = Guid.NewGuid() };
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        }
        if (reason == "changed-site") await f.HandleTurn("quiero cambiar de sede", "site-change");
        if (reason == "occupied") f.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Workspace, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TimeSlotDto>());
        const string selection = "quiero esa hora";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.SelectReferencedTime,
            Act = DialogueAct.Correction, Evidence = [new("Act", selection, nameof(DialogueAct.Correction))] });
        await f.HandleTurn(selection, "invalid-reference");
        Assert.Null((await f.Context()).TargetTime); Assert.Empty(f.Bookings);
        f.EngineReads.Verify(e => e.CreateReservationAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mvp_service_typo_uses_existing_literal_evidence_instead_of_picking_another_offering(bool backed)
    {
        var f = Create(); SetMvpServices(f); await f.SeedDraft("RESERVATION");
        var state = await f.Context(); state.SelectedServiceId = null; state.CurrentStep = "COLLECT_SERVICE";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Service = "Evaluación odontológica",
            Evidence = backed ? [new("Service", "evaluación odontologixa", "Evaluación odontológica")] : [] });
        await f.HandleTurn("quiero una evaluación odontologixa", "typo-mvp");
        Assert.Equal(backed ? "evaluation" : null, (await f.Context()).SelectedServiceId); Assert.Empty(f.Bookings);
        Assert.Equal(1, f.AiCalls); Assert.DoesNotContain(f.Bookings.Values, r => r.ServiceId == "cleaning");
    }

    [Theory]
    [InlineData("la 2", "evaluation")]
    [InlineData("la 4", "cleaning")]
    public async Task Mvp_numbers_select_the_id_in_the_real_current_menu_not_the_pdf(string choice, string expected)
    {
        var f = Create(); SetMvpServices(f); await f.SeedDraft("RESERVATION");
        var state = await f.Context(); state.SelectedServiceId = null; state.CurrentStep = "COLLECT_SERVICE";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("continuemos", "menu-mvp");
        state = await f.Context(); Assert.Equal(ExpectedReply.Selection, state.CurrentQuestion?.Expected);
        Assert.Equal("evaluation", state.RecoveryOptions[1].Key); Assert.Equal("cleaning", state.RecoveryOptions[3].Key);
        Assert.Contains("2. Evaluación odontológica", f.Sent["menu-mvp"].Content);
        Assert.Contains("4. Limpieza dental", f.Sent["menu-mvp"].Content);
        await f.HandleTurn(choice, "number-mvp");
        Assert.Equal(expected, (await f.Context()).SelectedServiceId); Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    private static void SetMvpServices(AssistantConversationTests.Fixture f)
    {
        f.Services.Clear();
        foreach (var (id, name) in new[] { ("advice", "Asesoría preventiva"), ("evaluation", "Evaluación odontológica"),
            ("fluoride", "Fluorización"), ("cleaning", "Limpieza dental"), ("restoration", "Restauración con resina simple") })
            f.Services.Add(new ServiceDto { Id = id, Name = name, RequiresReservation = id != "restoration", DurationInMinutes = id == "restoration" ? null : 30 });
    }
}
