using Moq;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

// These are handler/flow regressions with synthetic workspace data. Enqueued
// interpretations exercise the backend contract, not a real Gemini/Groq model.
public sealed class VirtualSecretaryClosureTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true, intelligentCommerce: true)
        { Now = new(2026, 10, 27, 13, 15, 0, DateTimeKind.Utc) };

    private static async Task Review(AssistantConversationTests.Fixture f)
    {
        await f.SeedDraft("RESERVATION");
        var state = await f.Context(); state.TargetDate = "2026-10-28"; state.TargetTime = "16:00";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("continuemos", "review");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Global_catalog_does_not_inherit_the_booking_site_and_keeps_pagination(bool selectedSite)
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var state = await f.Context();
        if (!selectedSite) { state.SelectedLocationId = null; state.CurrentStep = "COLLECT_LOCATION"; }
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        f.Services.AddRange(Enumerable.Range(2, 4).Select(i => new ServiceDto { Id = "extra-" + i, Name = "Servicio " + i,
            RequiresReservation = true, DurationInMinutes = 30, LocationScope = "SPECIFIC", LocationIds = ["sede-b"] }));
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("¿Qué servicios ofrecen?", "catalog");
        var query = f.Understood.Last().Interpretation.OfferingQuery!;
        output.WriteLine(f.Sent["catalog"].Content);
        Assert.NotNull(query);
        Assert.Null(query.LocationId); Assert.Equal(6, query.TotalCount);
        Assert.Contains("Servicios del negocio", f.Sent["catalog"].Content);
        Assert.Contains("Servicio 2", f.Sent["catalog"].Content);
        Assert.Contains("Ver más", f.Sent["catalog"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        await f.HandleTurn("Ver más", "page");
        Assert.Contains("Servicio 5", f.Sent["page"].Content);
        Assert.Null(f.Understood.Last().Interpretation.OfferingQuery!.LocationId);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Explicit_site_query_returns_only_reservable_services_without_changing_the_draft()
    {
        var f = Create(); await Review(f);
        f.Services[1].RequiresReservation = false;
        f.Services.Add(new ServiceDto { Id = "other", Name = "Restauración", RequiresReservation = true,
            DurationInMinutes = 45, LocationScope = "SPECIFIC", LocationIds = ["sede-b"] });
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ServiceQuery, QueryKind = OfferingQueryKind.Broad,
            Location = "Sede Concepcion", Question = OfferingQuestion.Availability });
        await f.HandleTurn("¿Cuáles puedo reservar en Concepción?", "site-catalog");
        var query = f.Understood.Last().Interpretation.OfferingQuery!;
        output.WriteLine(f.Sent["site-catalog"].Content);
        Assert.NotNull(query);
        Assert.Equal("sede-a", query.LocationId); Assert.Single(query.Offerings);
        Assert.Contains("Limpieza dental", f.Sent["site-catalog"].Content);
        Assert.DoesNotContain("*Limpieza profunda*", f.Sent["site-catalog"].Content);
        Assert.DoesNotContain("*Restauración*", f.Sent["site-catalog"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Global_query_explains_real_scope_and_non_reservable_configuration()
    {
        var f = Create(); await Review(f);
        f.Services[1].RequiresReservation = false;
        f.Services.Add(new ServiceDto { Id = "other", Name = "Restauración", RequiresReservation = true,
            DurationInMinutes = 45, LocationScope = "SPECIFIC", LocationIds = ["sede-b"] });
        await f.HandleTurn("¿Qué servicios ofrecen?", "scope");
        var text = f.Sent["scope"].Content;
        output.WriteLine(text);
        Assert.Contains("3 servicios", text); Assert.Contains("1 se puede reservar en Sede Concepcion", text);
        Assert.Contains("Limpieza profunda: no requiere reserva", text);
        Assert.Contains("Restauración: se ofrece para reservas en Sede San Jeronimo", text);
        Assert.Equal("*Servicios del negocio*\n\n"
            + "• *Limpieza dental*\n  S/ 0.00 · 30 min\n\n"
            + "• *Limpieza profunda*\n  S/ 0.00 · 30 min\n\n"
            + "• *Restauración*\n  S/ 0.00 · 45 min\n\n"
            + "El catálogo general puede incluir servicios de distintas sedes. Para reservar, comprobaré el servicio y la hora en la sede que elijas.\n\n"
            + "El catálogo general actual tiene 3 servicios; 1 se puede reservar en Sede Concepcion.\n\n"
            + "• Limpieza profunda: no requiere reserva según su configuración actual.\n\n"
            + "• Restauración: se ofrece para reservas en Sede San Jeronimo.\n\n"
            + "Tu reserva sigue guardada, sin confirmar. Puedes retomar la revisión del resumen o indicar el dato que deseas cambiar.", text);
        Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Legacy_booking_entry_answers_global_catalog_before_asking_for_booking_location()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var state = await f.Context(); state.SelectedLocationId = null; state.CurrentStep = "COLLECT_LOCATION";
        var before = AssistantConversationTests.Fixture.BusinessDraft(state);
        var response = await f.Booking.ProcessAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, "chat", state,
            new() { Intent = ConversationIntent.ServiceQuery, QueryKind = OfferingQueryKind.Broad }, "Ana", "legacy", default);
        Assert.Contains("*Limpieza dental*", response); Assert.Contains("*Limpieza profunda*", response);
        Assert.DoesNotContain("¿En cuál sede deseas consultar", response);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(state)); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Edit_field_then_compact_site_keeps_compatible_data_and_revalidates_the_exact_time()
    {
        var f = Create(); await Review(f);
        await f.HandleTurn("Editar", "edit");
        Assert.Equal(ExpectedReply.EditReservationField, (await f.Context()).CurrentQuestion!.Expected);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.ChangeLocation,
            Act = DialogueAct.Correction, Evidence = [new("Act", "La sede", nameof(DialogueAct.Correction))] });
        await f.HandleTurn("La sede", "field");
        Assert.Equal("COLLECT_LOCATION", (await f.Context()).CurrentStep);
        await f.HandleTurn("Sanjeronimo", "site");
        var state = await f.Context();
        Assert.Equal("sede-b", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("2026-10-28", state.TargetDate); Assert.Equal("16:00", state.TargetTime); Assert.Equal("Ana Pérez", state.RealCustomerName);
        Assert.Equal(ExpectedReply.ConfirmReservation, state.CurrentQuestion!.Expected);
        Assert.Contains("Sede San Jeronimo", f.Sent["site"].Content);
        f.EngineReads.Verify(e => e.GetAvailabilityAtAsync(f.Workspace, "sede-b", "service-a",
            It.Is<DateTime>(d => d.ToString("yyyy-MM-dd HH:mm") == "2026-10-28 16:00"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Changed_site_with_unavailable_time_requests_only_a_new_time()
    {
        var f = Create(); await Review(f);
        f.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Workspace, "sede-b", "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NexFlow.Application.Features.Reservations.TimeSlotDto>());
        await f.HandleTurn("Sanjeronimo", "site");
        var state = await f.Context();
        Assert.Equal("sede-b", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("2026-10-28", state.TargetDate); Assert.Equal("Ana Pérez", state.RealCustomerName);
        Assert.Null(state.TargetTime); Assert.Equal("COLLECT_TIME", state.CurrentStep);
        Assert.Contains("no está disponible", f.Sent["site"].Content); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Ordinal_after_factual_interruption_uses_the_still_current_reservation_list()
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var state = await f.Context();
        state.SelectedServiceId = null; state.CurrentStep = "COLLECT_SERVICE";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("continuemos", "list");
        var first = (await f.Context()).RecoveryOptions[0];
        await f.HandleTurn("¿Qué servicios ofrecen?", "catalog");
        await f.HandleTurn("La 1", "choice");
        Assert.Equal(first.Key, (await f.Context()).SelectedServiceId); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("RESERVATION")]
    [InlineData("ORDER")]
    public async Task Ambiguous_semantic_answer_never_writes_or_changes_a_draft(string goal)
    {
        var f = Create(); await f.SeedDraft(goal);
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = goal == "ORDER" ? ConversationIntent.Order : ConversationIntent.Reservation,
            RequiresClarification = true });
        await f.HandleTurn("Quizá eso, aunque todavía no lo tengo claro", "ambiguous");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        Assert.DoesNotContain("está confirmada", f.Sent["ambiguous"].Content);
    }
}
