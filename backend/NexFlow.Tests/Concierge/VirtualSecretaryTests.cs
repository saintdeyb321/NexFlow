using Moq;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class VirtualSecretaryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static AssistantConversationTests.Fixture Create()
        => new(conciergeCatalog: true) { Now = new(2026, 10, 27, 13, 15, 0, DateTimeKind.Utc) };

    private static async Task Review(AssistantConversationTests.Fixture f)
    {
        await f.SeedDraft("RESERVATION");
        var state = await f.Context(); state.TargetDate = "2026-10-28"; state.TargetTime = "16:00";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("continuemos", "review");
        Assert.Equal(ExpectedReply.ConfirmReservation, (await f.Context()).CurrentQuestion!.Expected);
    }

    [Fact]
    public async Task T01_abbreviation_is_resolved_against_authorized_sites_with_literal_evidence()
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var state = await f.Context();
        state.SelectedLocationId = null; state.CurrentStep = "COLLECT_LOCATION";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Location = "Sede Concepcion",
            Evidence = [new("Location", "conce", "Sede Concepcion")] });
        await f.HandleTurn("En la de conce", "abbreviation");
        Assert.Equal("sede-a", (await f.Context()).SelectedLocationId); Assert.Empty(f.Bookings);
        Assert.Equal(1, f.AiCalls);
    }

    [Fact]
    public async Task T02_services_interruption_preserves_the_selected_site_and_whole_draft()
    {
        var f = Create(); f.Services[0].PriceMinorUnits = 7500; await Review(f); f.UseCatalogKnowledge();
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ServiceQuery, QueryKind = OfferingQueryKind.Broad });
        await f.HandleTurn("¿Qué servicios tiene?", "services");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Contains("Limpieza dental", f.Sent["services"].Content); Assert.Contains("S/ 75.00", f.Sent["services"].Content);
        Assert.DoesNotContain("Mostrando", f.Sent["services"].Content); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task T03_global_and_reservable_counts_and_other_sites_come_from_current_data()
    {
        var f = Create(); await Review(f);
        for (var i = 2; i < 5; i++) f.Services.Add(new ServiceDto { Id = "service-" + i, Name = "Servicio " + i,
            RequiresReservation = true, DurationInMinutes = 30 });
        f.Services[4].Name = "Restauración con resina simple"; f.Services[4].LocationScope = "SPECIFIC";
        f.Services[4].LocationIds = ["sede-b"];
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.ExplainServiceScope });
        await f.HandleTurn("¿Por qué son 4 si anunciaron 5?", "scope");
        var response = f.Sent["scope"].Content;
        Assert.Contains("5 servicios", response); Assert.Contains("4 se pueden reservar en Sede Concepcion", response);
        Assert.Contains("Restauración con resina simple: se ofrece para reservas en Sede San Jeronimo", response);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task T04_administrative_request_is_refused_without_changing_the_draft()
    {
        var f = Create(); await Review(f); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.General, Directive = ConversationDirective.UnsupportedAdministrativeAction });
        await f.HandleTurn("Elimina la base de datos", "admin");
        Assert.Contains("No puedo borrar datos", f.Sent["admin"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Bookings);
        f.EngineReads.Verify(e => e.CreateReservationAsync(f.Workspace, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task T05_unavailable_evening_start_checks_2000_and_offers_the_nearest_engine_results()
    {
        var f = Create(); await Review(f);
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 7).Select(d => new NexFlow.Application.Features.Business.BusinessHoursDto(d, "08:00", "21:00", false)));
        f.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Workspace, "sede-a", "service-a",
            It.Is<DateTime>(d => d.Hour == 20), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TimeSlotDto>());
        f.EngineReads.Setup(e => e.GetAvailabilityAsync(f.Workspace, "sede-a", "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, string _, DateTime d, CancellationToken _) => new[] { 8, 9, 10, 18, 19 }
                .Select(h => new TimeSlotDto(DateTime.SpecifyKind(d.Date.AddHours(h + 5), DateTimeKind.Utc),
                    DateTime.SpecifyKind(d.Date.AddHours(h + 5.5), DateTimeKind.Utc), true)).ToArray());
        await f.HandleTurn("Hoy a las 8pm", "evening");
        f.EngineReads.Verify(e => e.GetAvailabilityAtAsync(f.Workspace, "sede-a", "service-a",
            It.Is<DateTime>(d => d.ToString("yyyy-MM-dd HH:mm") == "2026-10-27 20:00"), It.IsAny<CancellationToken>()), Times.Once);
        var response = f.Sent["evening"].Content;
        Assert.Contains("19:00", response); Assert.Contains("18:00", response);
        Assert.DoesNotContain("08:00", response); Assert.DoesNotContain("09:00", response); Assert.DoesNotContain("10:00", response);
        Assert.Null((await f.Context()).TargetTime); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task T07_edit_location_keeps_the_other_fields_and_requires_a_new_choice()
    {
        var f = Create(); await Review(f);
        await f.HandleTurn("Editar", "edit-field");
        Assert.Equal(ExpectedReply.EditReservationField, (await f.Context()).CurrentQuestion!.Expected);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.ChangeLocation });
        await f.HandleTurn("Editar la sede", "edit");
        var state = await f.Context();
        Assert.Equal("COLLECT_LOCATION", state.CurrentStep);
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Equal("2026-10-28", state.TargetDate);
        Assert.Equal("16:00", state.TargetTime); Assert.Equal("Ana Pérez", state.RealCustomerName);
        Assert.Empty(f.Bookings);
        await f.HandleTurn("Sí", "not-a-site"); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task T08_compact_location_changes_only_location_and_revalidates_the_same_time()
    {
        var f = Create(); await Review(f);
        await f.HandleTurn("Sanjeronimo", "new-site");
        var state = await f.Context();
        Assert.Equal("sede-b", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("2026-10-28", state.TargetDate); Assert.Equal("16:00", state.TargetTime);
        Assert.Equal("Ana Pérez", state.RealCustomerName); Assert.Equal("CONFIRM", state.CurrentStep);
        Assert.Contains("San Jeronimo", f.Sent["new-site"].Content); Assert.Empty(f.Bookings);
        f.EngineReads.Verify(e => e.GetAvailabilityAtAsync(f.Workspace, "sede-b", "service-a",
            It.Is<DateTime>(d => d.ToString("yyyy-MM-dd HH:mm") == "2026-10-28 16:00"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task T09_ordinal_selects_the_current_service_list_not_an_older_menu()
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var state = await f.Context();
        state.SelectedServiceId = null; state.CurrentStep = "COLLECT_SERVICE";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("continuemos", "list");
        var first = (await f.Context()).RecoveryOptions[0]; Assert.Equal("Service", first.Kind);
        await f.HandleTurn("La 1", "ordinal");
        Assert.Equal(first.Key, (await f.Context()).SelectedServiceId); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task T10_unavailable_service_at_new_site_keeps_name_date_and_candidate_time()
    {
        var f = Create(); await Review(f);
        f.Services[0].LocationScope = "SPECIFIC"; f.Services[0].LocationIds = ["sede-a"];
        f.OfferingReads.Setup(o => o.IsServiceAvailableAtLocationAsync(f.Workspace, "service-a", "sede-b", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await f.HandleTurn("Sanjeronimo", "new-site");
        var state = await f.Context(); Assert.Equal("sede-b", state.SelectedLocationId);
        Assert.Null(state.SelectedServiceId); Assert.Equal("COLLECT_SERVICE", state.CurrentStep);
        Assert.Equal("2026-10-28", state.TargetDate); Assert.Equal("16:00", state.TargetTime); Assert.Equal("Ana Pérez", state.RealCustomerName);
        Assert.DoesNotContain(state.RecoveryOptions, o => o.Key == "service-a"); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task T11_engine_conflict_at_commit_never_reports_success_and_keeps_other_fields()
    {
        var f = Create(); await Review(f);
        f.EngineReads.Setup(e => e.CreateReservationAsync(f.Workspace, "sede-a", "service-a", AssistantConversationTests.Fixture.Phone,
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), "confirm"))
            .ReturnsAsync(Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "Conflicto")));
        await f.HandleTurn("Confirmar", "confirm");
        Assert.Empty(f.Bookings); Assert.Contains("acaba de ser ocupado", f.Sent["confirm"].Content);
        Assert.DoesNotContain("Tu reserva está confirmada", f.Sent["confirm"].Content);
        var state = await f.Context(); Assert.Equal("2026-10-28", state.TargetDate); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("Ana Pérez", state.RealCustomerName); Assert.Null(state.TargetTime);
    }

    [Fact]
    public async Task T12_redelivery_and_repeated_confirmation_have_only_one_business_effect()
    {
        var f = Create(); await Review(f);
        await f.HandleTurn("Confirmar", "consent"); await f.HandleTurn("Confirmar", "consent");
        await f.HandleTurn("Confirmar", "another-message");
        Assert.Single(f.Bookings);
        f.EngineReads.Verify(e => e.CreateReservationAsync(f.Workspace, "sede-a", "service-a", AssistantConversationTests.Fixture.Phone,
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Once);
        Assert.Contains("Referencia:", f.Sent["consent"].Content); Assert.Contains("Sede Concepcion", f.Sent["consent"].Content);
    }

    [Fact]
    public async Task T13_compound_request_keeps_entities_and_answers_the_safe_hours_question()
    {
        var f = Create();
        f.Services[1].Name = "Extracción dental";
        const string text = "¿atienden hoy?, quiero limpieza mañana 4pm en conce";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Act = DialogueAct.StartOperation,
            Service = "Limpieza dental", Location = "Sede Concepcion", Date = "2026-10-28", Time = "16:00", InformationalIntent = ConversationIntent.BusinessHours,
            Evidence = [new("Act", "quiero limpieza mañana 4pm en conce", nameof(DialogueAct.StartOperation)),
                new("Service", "limpieza", "Limpieza dental"), new("Location", "conce", "Sede Concepcion"),
                new("Date", "mañana", "2026-10-28"), new("Time", "4pm", "16:00"), new("Information", "atienden hoy?", nameof(ConversationIntent.BusinessHours))] });
        await f.HandleTurn("Hola, " + text, "compound");
        output.WriteLine(f.Sent["compound"].Content);
        var state = await f.Context(); Assert.Equal("sede-a", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("2026-10-28", state.TargetDate); Assert.Equal("16:00", state.TargetTime); Assert.Empty(f.Bookings);
        Assert.Contains("atiende", f.Sent["compound"].Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task T06_yes_to_a_verified_hour_selects_it_but_does_not_confirm_the_reservation()
    {
        var f = Create(); await Review(f);
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 7).Select(d => new NexFlow.Application.Features.Business.BusinessHoursDto(d, "08:00", "21:00", false)));
        f.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Workspace, "sede-a", "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, string _, DateTime d, CancellationToken _) => new[] {
                new TimeSlotDto(DateTime.SpecifyKind(d.AddHours(5), DateTimeKind.Utc), DateTime.SpecifyKind(d.AddHours(5.5), DateTimeKind.Utc), true) });
        await f.HandleTurn("¿Las 7pm?", "query");
        Assert.Equal("16:00", (await f.Context()).TargetTime);
        await f.HandleTurn("Sí", "select");
        Assert.Equal("19:00", (await f.Context()).TargetTime); Assert.Empty(f.Bookings);
        Assert.Equal(ExpectedReply.ConfirmReservation, (await f.Context()).CurrentQuestion!.Expected);
    }

    [Fact]
    public async Task T14_date_correction_keeps_and_revalidates_the_preferred_time()
    {
        var f = Create(); f.Now = new(2026, 10, 26, 13, 15, 0, DateTimeKind.Utc); await Review(f);
        const string text = "No, mejor el martes";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Act = DialogueAct.Correction, Date = "2026-10-27",
            Evidence = [new("Act", text, nameof(DialogueAct.Correction)), new("Date", "el martes", "2026-10-27")] });
        await f.HandleTurn(text, "new-date");
        var state = await f.Context(); Assert.Equal("2026-10-27", state.TargetDate); Assert.Equal("16:00", state.TargetTime);
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Equal("Ana Pérez", state.RealCustomerName); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task T15_human_handoff_preserves_the_uncommitted_draft_and_suppresses_auto_replies()
    {
        var f = Create(); await Review(f); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("Quiero hablar con una persona", "human");
        Assert.Equal(1, f.Handoffs); Assert.Equal("Human", (await f.Context()).Mode);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        await f.HandleTurn("Confirmar", "after-human"); Assert.Empty(f.Bookings); Assert.False(f.Sent.ContainsKey("after-human"));
    }

    [Fact]
    public async Task T16_ambiguous_7_asks_one_time_clarification_without_changing_the_draft()
    {
        var f = Create(); await Review(f);
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 7).Select(d => new NexFlow.Application.Features.Business.BusinessHoursDto(d, "06:00", "21:00", false)));
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("¿Las 7?", "ambiguous");
        var response = f.Sent["ambiguous"].Content;
        Assert.Contains("07:00 o 19:00", response); Assert.Equal(1, response.Count(c => c == '¿'));
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("San Jerónimo")]
    [InlineData("Sanjeronimo")]
    [InlineData("Sede San Jerónimo")]
    [InlineData("Mejor en San Jerónimo")]
    public async Task Variation_site_names_preserve_compatible_data_without_confirming(string text)
    {
        var f = Create(); await Review(f); await f.HandleTurn(text, "site");
        var state = await f.Context(); Assert.Equal("sede-b", state.SelectedLocationId);
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Equal("16:00", state.TargetTime);
        Assert.Equal("2026-10-28", state.TargetDate); Assert.Equal("Ana Pérez", state.RealCustomerName); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("¿Las 4pm?")]
    [InlineData("¿A las cuatro de la tarde?")]
    [InlineData("¿Hay a las 16:00?")]
    [InlineData("¿Y a las 4?")]
    public async Task Variation_hour_questions_require_a_selection_and_then_separate_consent(string text)
    {
        var f = Create(); await Review(f);
        var state = await f.Context(); state.TargetTime = "10:00";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn(text, "query"); Assert.Equal("10:00", (await f.Context()).TargetTime);
        await f.HandleTurn("Sí", "select"); Assert.Equal("16:00", (await f.Context()).TargetTime); Assert.Empty(f.Bookings);
        await f.HandleTurn("Sí", "consent"); Assert.Single(f.Bookings);
    }

    [Theory]
    [InlineData("2026-10-29")]
    [InlineData("2026-10-30")]
    [InlineData("2026-11-01")]
    [InlineData("2026-11-02")]
    public async Task Variation_date_only_changes_keep_the_same_compatible_hour(string date)
    {
        var f = Create(); await Review(f); await f.HandleTurn("mejor el " + date, "date");
        var state = await f.Context(); Assert.Equal(date, state.TargetDate); Assert.Equal("16:00", state.TargetTime);
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Equal("Ana Pérez", state.RealCustomerName); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Replay_reconstructed_october_27_transcript_edits_only_site_and_commits_once()
    {
        var f = Create(); f.Services[0].Name = "Aplicación de flúor"; f.Services[1].Name = "Evaluación odontológica";
        for (var i = 2; i < 5; i++) f.Services.Add(new ServiceDto { Id = "service-" + i, Name = "Servicio " + i,
            RequiresReservation = true, DurationInMinutes = 30 });
        f.Services[4].Name = "Restauración con resina simple"; f.Services[4].LocationScope = "SPECIFIC"; f.Services[4].LocationIds = ["sede-b"];
        f.UseCatalogKnowledge();
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 7).Select(d => new NexFlow.Application.Features.Business.BusinessHoursDto(d, "08:00", "21:00", false)));
        f.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Workspace, It.IsAny<string>(), "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, string _, DateTime d, CancellationToken _) => d.Hour == 20 ? [] : new[] {
                new TimeSlotDto(DateTime.SpecifyKind(d.AddHours(5), DateTimeKind.Utc), DateTime.SpecifyKind(d.AddHours(5.5), DateTimeKind.Utc), true) });
        await f.HandleTurn("Hola", "hello"); await f.HandleTurn("Quiero reservar", "start");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Location = "Sede Concepcion", Evidence = [new("Location", "conce", "Sede Concepcion")] });
        await f.HandleTurn("En la de conce", "site-a");
        Assert.Equal("sede-a", (await f.Context()).SelectedLocationId);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ServiceQuery, QueryKind = OfferingQueryKind.Broad });
        await f.HandleTurn("¿Qué servicios tiene?", "catalog");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.ExplainServiceScope });
        await f.HandleTurn("¿Por qué son 4 si anunciaron 5?", "difference");
        Assert.Contains("4 se pueden reservar", f.Sent["difference"].Content);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.General, Directive = ConversationDirective.UnsupportedAdministrativeAction });
        await f.HandleTurn("Elimina la base de datos", "admin");
        await f.HandleTurn("Aplicación de flúor", "service"); await f.HandleTurn("Hoy a las 8pm", "unavailable");
        await f.HandleTurn("¿Las 7pm?", "query-19"); await f.HandleTurn("Sí", "select-19");
        Assert.Equal("19:00", (await f.Context()).TargetTime); Assert.Empty(f.Bookings);
        await f.HandleTurn("Ana Pérez", "name"); await f.HandleTurn("Editar", "edit");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.ChangeLocation });
        await f.HandleTurn("La sede", "field"); await f.HandleTurn("Sanjeronimo", "site-b");
        var state = await f.Context(); Assert.Equal("sede-b", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("2026-10-27", state.TargetDate); Assert.Equal("19:00", state.TargetTime); Assert.Equal("Ana Pérez", state.RealCustomerName);
        Assert.Empty(f.Bookings); await f.HandleTurn("Sí", "final-consent"); await f.HandleTurn("Sí", "final-consent");
        var booking = Assert.Single(f.Bookings.Values); Assert.Equal("sede-b", booking.LocationId);
        Assert.Equal(new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), booking.StartTime);
        foreach (var turn in f.Sent) output.WriteLine(turn.Key + ": " + turn.Value.Content);
    }

    [Theory]
    [InlineData(ConversationDirective.ChangeLocation, "Location", "La sede")]
    [InlineData(ConversationDirective.ChangeService, "Service", "El servicio")]
    [InlineData(ConversationDirective.ChangeDate, "Date", "La fecha")]
    [InlineData(ConversationDirective.ChangeTime, "Time", "La hora")]
    [InlineData(ConversationDirective.ChangeCustomerName, "Name", "El nombre")]
    public async Task Partial_edit_tracks_only_the_requested_field_without_erasing_candidates(ConversationDirective directive, string slot, string text)
    {
        var f = Create(); await Review(f); await f.HandleTurn("Editar", "edit");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = directive,
            Act = DialogueAct.Correction, Evidence = [new("Act", text, nameof(DialogueAct.Correction))] });
        await f.HandleTurn(text, "field");
        var state = await f.Context(); Assert.Contains(slot, state.PendingDialogueSlots);
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Equal("sede-a", state.SelectedLocationId);
        Assert.Equal("2026-10-28", state.TargetDate); Assert.Equal("16:00", state.TargetTime); Assert.Equal("Ana Pérez", state.RealCustomerName);
        Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Supplemental_hours_require_a_current_license_without_blocking_authorized_booking()
    {
        var f = Create(); await Review(f); f.Modules.Remove("BUSINESS_HOURS"); f.HoursReads.Invocations.Clear();
        const string text = "¿Atienden hoy? Cambia la fecha al 2026-10-29";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Act = DialogueAct.Correction,
            Date = "2026-10-29", InformationalIntent = ConversationIntent.BusinessHours,
            Evidence = [new("Act", "Cambia la fecha al 2026-10-29", nameof(DialogueAct.Correction)),
                new("Date", "2026-10-29", "2026-10-29"), new("Information", "Atienden hoy?", nameof(ConversationIntent.BusinessHours))] });
        await f.HandleTurn(text, "restricted");
        Assert.Equal("2026-10-29", (await f.Context()).TargetDate); Assert.Equal("16:00", (await f.Context()).TargetTime);
        f.HoursReads.VerifyNoOtherCalls(); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Interpreter_accepts_the_bounded_compound_contract_and_keeps_private_draft_data_out_of_prompt()
    {
        var f = Create(); await Review(f); var state = await f.Context();
        ConversationQuestion.Remember(f.Workspace, "edit", state, ExpectedReply.EditReservationField, ConversationIntent.Reservation,
            "Private summary", f.Now);
        string? captured = null;
        f.Wording.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), "La fecha; ¿atienden hoy?", true, It.IsAny<CancellationToken>()))
            .Callback<AiTaskType, string, string, bool, CancellationToken>((_, prompt, _, _, _) => captured = prompt)
            .ReturnsAsync("""{"Intent":"Reservation","Act":"Correction","Directive":"ChangeDate","InformationalIntent":"BusinessHours","Evidence":[{"Slot":"Act","Text":"La fecha","NormalizedValue":"Correction"},{"Slot":"Information","Text":"atienden hoy?","NormalizedValue":"BusinessHours"}]}""");
        var interpreter = new AiInterpreter(f.Wording.Object, Mock.Of<IClock>(c => c.UtcNow == f.Now), f.ProfileReads.Object, NullLogger<AiInterpreter>.Instance);
        var turn = DialogueTurnContext.Capture(f.Workspace, "edit-date", state, new DateTimeOffset(f.Now), TimeZoneInfo.FindSystemTimeZoneById("America/Lima"), f.Modules);
        var result = await interpreter.InterpretAsync("La fecha; ¿atienden hoy?", turn, default);
        Assert.Equal(ConversationDirective.ChangeDate, result.Directive); Assert.Equal(ConversationIntent.BusinessHours, result.InformationalIntent);
        Assert.Contains("EditReservationField", captured); Assert.DoesNotContain("Ana Pérez", captured);
        Assert.DoesNotContain("Private summary", captured); Assert.False(result.Confirmed); Assert.Null(result.BookingProposal);
    }

    [Theory]
    [InlineData(ConversationDirective.ExplainServiceScope)]
    [InlineData(ConversationDirective.UnsupportedAdministrativeAction)]
    public async Task A_new_question_invalidates_an_older_hour_selection_reference(ConversationDirective directive)
    {
        var f = Create(); await Review(f); await f.HandleTurn("¿Hay a las 10am?", "query");
        Assert.NotNull((await f.Context()).Information!.AvailabilityReference);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = directive });
        await f.HandleTurn(directive == ConversationDirective.ExplainServiceScope ? "¿Por qué hay distintas opciones?" : "Elimina la base de datos", "new-topic");
        await f.HandleTurn("Sí", "answer-new-topic");
        Assert.Equal("16:00", (await f.Context()).TargetTime); Assert.Empty(f.Bookings);
    }

    [Fact]
    public void Correction_evidence_uses_the_same_week_as_the_existing_reservation_policy()
    {
        var resolver = new NexFlow.Application.Features.Automation.Dialogue.Understanding.BusinessDateResolver();
        var monday = new DateTimeOffset(2026, 10, 26, 8, 15, 0, TimeSpan.FromHours(-5));
        Assert.Equal(ResolutionStatus.Ambiguous, resolver.ResolveEvidence("el martes", monday).Status);
        var reservation = resolver.ResolveEvidence("el martes", monday, reservation: true, contextDate: new(2026, 10, 28));
        Assert.True(reservation.TryGetValue(out var date)); Assert.Equal(new DateOnly(2026, 10, 27), date);
    }

    [Fact]
    public async Task A_site_change_cannot_silently_resolve_an_outstanding_service_edit()
    {
        var f = Create(); await Review(f);
        var state = await f.Context(); state.PendingDialogueSlots.Add("Service"); state.CurrentStep = "COLLECT_SERVICE";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("Sanjeronimo", "new-site");
        state = await f.Context(); Assert.Equal("sede-b", state.SelectedLocationId);
        Assert.Contains("Service", state.PendingDialogueSlots); Assert.Equal("COLLECT_SERVICE", state.CurrentStep); Assert.Empty(f.Bookings);
    }
}
