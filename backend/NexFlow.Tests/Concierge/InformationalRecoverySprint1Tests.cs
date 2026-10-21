using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Knowledge;
using Xunit;

namespace NexFlow.Tests.Concierge;

// Real handler/recovery/orchestrator/knowledge and durable context repository, synthetic RPC/data only.
public sealed class InformationalRecoverySprint1Tests
{
    private static AssistantConversationTests.Fixture Create()
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true) { Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc) };
        f.ProfileReads.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessProfileDto("Clínica autorizada", "PRIVATE-TAX-ID", "public@example.test", "51900000000", "Atención dental del negocio", "America/Lima"));
        var knowledge = new KnowledgeService(f.ProfileReads.Object, f.LocationReads.Object, f.HoursReads.Object,
            Mock.Of<IFaqRepository>(MockBehavior.Strict), f.OfferingReads.Object, NullLogger<KnowledgeService>.Instance);
        f.KnowledgeReads.Setup(k => k.QueryAsync(f.Workspace, It.Is<BusinessKnowledgeSnapshot>(s => s.WorkspaceId == f.Workspace),
            It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>()))
            .Returns((Guid w, BusinessKnowledgeSnapshot snapshot, KnowledgeQuery query, CancellationToken ct) => {
                f.FactualQueries.Add((w, query.Topic)); return knowledge.QueryAsync(w, snapshot, query, ct);
            });
        return f;
    }

    [Theory]
    [InlineData("Quiero información de la clínica")]
    [InlineData("Hola, quiero información")]
    [InlineData("Quiero información")]
    public async Task Public_profile_is_reached_without_menu_ai_or_private_tax_id(string message)
    {
        var f = Create(); f.Modules.Clear();
        await f.HandleTurn(message, "profile");
        var response = f.Sent["profile"].Content;
        Assert.Contains("Clínica autorizada", response); Assert.Contains("Atención dental", response);
        Assert.DoesNotContain("PRIVATE-TAX-ID", response); Assert.DoesNotContain("opciones disponibles", response);
        Assert.Equal(KnowledgeTopic.Profile, Assert.Single(f.FactualQueries).Topic);
        Assert.Empty(f.OfferingReads.Invocations); Assert.Empty(f.HoursReads.Invocations);
        Assert.Equal(0, f.AiCalls); Assert.Null((await f.Context()).CurrentGoal);
    }

    [Fact]
    public async Task Empty_global_with_two_schedules_persists_a_choice_and_repairs_scope_before_numeric_selection()
    {
        var f = Create();
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BusinessHoursDto>());
        await f.HandleTurn("horarios", "hours"); // Existing handler shortcut must also persist the informational scope.
        Assert.Contains("¿De qué sede", f.Sent["hours"].Content);
        Assert.DoesNotContain("no hay horarios", f.Sent["hours"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Location", (await f.Context()).Information!.Clarification);
        Assert.Single(f.LocationReads.Invocations);
        f.HoursReads.Verify(h => h.GetBusinessHoursAsync(f.Workspace, It.Is<string?>(s => s != null), It.IsAny<CancellationToken>()), Times.Never);
        await f.HandleTurn("¿Cuál sede? Si ni me preguntaste", "repair");
        Assert.Contains("¿De qué sede", f.Sent["repair"].Content); Assert.DoesNotContain("Dirección", f.Sent["repair"].Content);
        await f.HandleTurn("1", "select");
        Assert.Contains("Sede Concepcion", f.Sent["select"].Content); Assert.Contains("08:00 – 18:00", f.Sent["select"].Content);
        Assert.Equal("sede-a", (await f.Context()).Information!.LocationId);
        Assert.Null((await f.Context()).SelectedLocationId); Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
        await f.HandleTurn("cuáles son sus horarios", "follow-up");
        Assert.Contains("Sede Concepcion", f.Sent["follow-up"].Content);
    }

    [Fact]
    public async Task Explicit_location_overrides_valid_context_and_stale_context_falls_back_to_real_global()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, "sede-b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new BusinessHoursDto(1, "09:00", "12:00", false) });
        await f.HandleTurn("qué horarios tienen en San Jerónimo", "explicit");
        Assert.Contains("Sede San Jeronimo", f.Sent["explicit"].Content); Assert.Contains("09:00 – 12:00", f.Sent["explicit"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        var state = await f.Context(); state.SelectedLocationId = "foreign-or-deleted";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("cuáles son sus horarios", "stale");
        Assert.Contains("Horario general", f.Sent["stale"].Content);
        f.HoursReads.Verify(h => h.GetBusinessHoursAsync(f.Workspace, "foreign-or-deleted", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Grouping_preserves_closed_missing_and_different_days_and_global_does_not_fan_out()
    {
        var f = Create();
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 5).Select(d => new BusinessHoursDto(d, "08:00", "18:00", false))
                .Append(new BusinessHoursDto(6, "", "", true)));
        await f.HandleTurn("cuáles son sus horarios", "hours");
        var response = f.Sent["hours"].Content;
        Assert.Contains("Lunes a viernes: 08:00 – 18:00", response);
        Assert.Contains("Sábado: Cerrado", response); Assert.Contains("Domingo: Sin horario registrado", response);
        f.HoursReads.Verify(h => h.GetBusinessHoursAsync(f.Workspace, It.Is<string?>(id => id != null), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task General_question_lists_at_most_five_locations_without_reading_twenty_calendars()
    {
        var f = Create();
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BusinessHoursDto>());
        f.LocationReads.Setup(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 20).Select(i => new LocationDto($"office-{i}", $"Oficina {i:00}", "", null, null, false)).ToArray());
        await f.HandleTurn("cuáles son sus horarios", "list");
        Assert.Equal(5, (await f.Context()).Information!.Locations.Count);
        Assert.DoesNotContain("6. ", f.Sent["list"].Content);
        f.HoursReads.Verify(h => h.GetBusinessHoursAsync(f.Workspace, It.Is<string?>(s => s != null), It.IsAny<CancellationToken>()), Times.Never);
        await f.HandleTurn("Oficina 20", "outside-page");
        Assert.Equal("office-20", (await f.Context()).Information!.LocationId);
        Assert.Contains("Oficina 20", f.Sent["outside-page"].Content); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Repair_reuses_factual_question_and_keeps_transactional_draft_and_last_question()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("Quiero información de la clínica", "profile");
        await f.HandleTurn("No te pedí eso", "repair");
        Assert.Contains("Conservamos los datos", f.Sent["repair"].Content);
        await f.HandleTurn("Yo pregunté por sus horarios", "correct-topic");
        Assert.Contains("Horario de atención de Sede Concepcion", f.Sent["correct-topic"].Content);
        await f.HandleTurn("Eso ya te lo dije", "repeat");
        Assert.Contains("Conservamos los datos", f.Sent["repeat"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Protest_after_location_answer_asks_a_targeted_question_instead_of_repeating_directions()
    {
        var f = Create();
        await f.HandleTurn("donde estan", "locations");
        Assert.Contains("Dirección", f.Sent["locations"].Content);
        var reads = f.FactualQueries.Count;
        await f.HandleTurn("yo no te pedí eso", "repair");
        Assert.Contains("ubicaciones", f.Sent["repair"].Content);
        Assert.DoesNotContain("Dirección", f.Sent["repair"].Content);
        Assert.Equal(reads, f.FactualQueries.Count); Assert.Equal("Topic", (await f.Context()).Information!.Clarification);
        await f.HandleTurn("yo pregunté por sus horarios", "correct");
        Assert.Contains("Horario general", f.Sent["correct"].Content); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Information_cannot_revalidate_an_expired_complete_booking_draft()
    {
        var f = Create(); await Ready(f); f.Now = f.Now.AddHours(25);
        await f.HandleTurn("Quiero información de la clínica", "info");
        Assert.Contains("Clínica autorizada", f.Sent["info"].Content);
        Assert.Equal("EXPIRE", (await f.Context()).PendingRecoveryAction);
        await f.HandleTurn("sí confirmo", "answer");
        Assert.Empty(f.Bookings); Assert.Null((await f.Context()).CurrentGoal); Assert.Null((await f.Context()).PendingRecoveryAction);
    }

    [Fact]
    public async Task Yes_to_a_pending_informational_choice_is_not_consent_to_an_older_booking_summary()
    {
        var f = Create(); await Ready(f);
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BusinessHoursDto>());
        await f.HandleTurn("horarios generales", "info");
        Assert.Equal("Location", (await f.Context()).Information!.Clarification);
        await f.HandleTurn("sí confirmo", "yes");
        Assert.Empty(f.Bookings); Assert.Equal("CONFIRM", (await f.Context()).CurrentStep);
        Assert.Contains("¿De qué sede", f.Sent["yes"].Content);
    }

    [Fact]
    public async Task Ambiguous_location_does_not_fall_back_to_global_or_choose_a_site()
    {
        var f = Create();
        await f.HandleTurn("qué horarios tienen en Concepción o San Jerónimo", "ambiguous");
        Assert.Contains("precisar la sede", f.Sent["ambiguous"].Content);
        Assert.DoesNotContain("08:00", f.Sent["ambiguous"].Content);
        Assert.Null((await f.Context()).Information!.LocationId); Assert.Empty(f.HoursReads.Invocations);
    }

    [Theory]
    [InlineData("quiero editar mi reserva")]
    [InlineData("quiero cambiar el horario de mi cita")]
    [InlineData("quiero cancelar mi reserva confirmada")]
    [InlineData("quiero consultar mi reserva")]
    public async Task Registered_reservation_request_does_not_start_creation_or_disclose_appointments(string message)
    {
        var f = Create();
        await f.HandleTurn(message, "manage");
        Assert.Contains("ya registrada", f.Sent["manage"].Content); Assert.Contains("asesor", f.Sent["manage"].Content);
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings);
        Assert.Empty(f.FactualQueries); Assert.Empty(f.EngineReads.Invocations); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Rejecting_a_new_operation_to_ask_information_does_not_discard_an_existing_draft()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("No quiero hacer una reserva, quiero información", "information");
        Assert.Contains("Clínica autorizada", f.Sent["information"].Content);
        await f.HandleTurn("quiero editar mi reserva confirmada", "manage");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("sí confirmos")]
    [InlineData("sí, pero no confirmo")]
    [InlineData("si hay disponibilidad confirmo")]
    [InlineData("¿sí?")]
    [InlineData("confirmo y cambia la sede")]
    public async Task Uncertain_consent_cannot_commit_even_a_complete_current_summary(string message)
    {
        var f = Create(); await Ready(f);
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn(message, "uncertain");
        Assert.Contains("No he confirmado", f.Sent["uncertain"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Exact_consent_commits_current_summary_once_and_does_not_confirm_without_a_summary()
    {
        var f = Create(); await Ready(f);
        await f.HandleTurn("Sí, confirmo", "confirm"); await f.HandleTurn("Sí, confirmo", "confirm");
        Assert.Single(f.Bookings); Assert.Equal(0, f.AiCalls);
        await f.HandleTurn("Sí, confirmo", "no-summary"); Assert.Single(f.Bookings);
        Assert.False(ConversationConfirmation.IsAffirmative("sí, confirmos"));
    }

    [Fact]
    public async Task Revocation_and_workspace_identity_prevent_information_reads_from_an_old_selection()
    {
        var f = Create();
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BusinessHoursDto>());
        await f.HandleTurn("horarios", "list"); f.Modules.Remove("BUSINESS_HOURS");
        f.HoursReads.Invocations.Clear(); f.LocationReads.Invocations.Clear(); f.KnowledgeReads.Invocations.Clear();
        await f.HandleTurn("1", "revoked");
        Assert.Contains("No hay información de horarios", f.Sent["revoked"].Content);
        Assert.Empty(f.HoursReads.Invocations); Assert.Empty(f.LocationReads.Invocations); Assert.Empty(f.KnowledgeReads.Invocations);
        var foreign = await f.Contexts.GetOrRecoverContextAsync(Guid.NewGuid(), AssistantConversationTests.Fixture.Phone, default);
        Assert.Null(foreign.Information); Assert.Null(foreign.CurrentGoal);
    }

    [Fact]
    public async Task License_is_rechecked_before_sending_recovered_facts_and_legacy_json_remains_readable()
    {
        var f = Create(); f.DuringComposition = () => { f.Modules.Remove("BUSINESS_HOURS"); return Task.CompletedTask; };
        await f.HandleTurn("cuáles son sus horarios", "revoked-during-turn");
        Assert.DoesNotContain("08:00", f.Sent["revoked-during-turn"].Content);
        Assert.Contains("No hay información de horarios", f.Sent["revoked-during-turn"].Content);
        var legacy = JsonSerializer.Deserialize<ConversationContextDto>("{\"CurrentGoal\":\"RESERVATION\",\"StateVersion\":\"old\"}")!;
        Assert.Null(legacy.Information); Assert.Equal("old", legacy.StateVersion);
    }

    [Fact]
    public async Task Location_lookup_outage_does_not_invent_hours_or_clear_the_draft()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.LocationReads.Setup(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("synthetic"));
        await f.HandleTurn("cuáles son sus horarios", "outage");
        Assert.Contains("No puedo consultar", f.Sent["outage"].Content); Assert.DoesNotContain("08:00", f.Sent["outage"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Unresolved_repairs_still_use_the_existing_bounded_human_handoff()
    {
        var f = Create(); await f.HandleTurn("Quiero información de la clínica", "profile");
        for (var i = 0; i < 3; i++) await f.HandleTurn("No te pedí eso", $"repair-{i}");
        Assert.Equal(1, f.Handoffs); Assert.Equal("Human", (await f.Context()).Mode);
        await f.HandleTurn("cuáles son sus horarios", "after-human");
        Assert.Equal(4, f.Sent.Count); Assert.Equal(0, f.AiCalls);
    }

    private static async Task Ready(AssistantConversationTests.Fixture f)
    {
        await f.SeedDraft("RESERVATION"); var state = await f.Context();
        state.CurrentStep = "CONFIRM"; state.TargetDate = "2026-10-22"; state.TargetTime = "16:00";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
    }
}
