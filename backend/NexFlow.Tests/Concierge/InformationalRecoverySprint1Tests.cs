using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Knowledge;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

// Real handler/recovery/orchestrator/knowledge and durable context repository, synthetic RPC/data only.
public sealed class InformationalRecoverySprint1Tests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("¿Qué horarios tienen?")]
    [InlineData("¿Cuál es la dirección del negocio?")]
    [InlineData("¿Qué servicios ofrecen?")]
    [InlineData("Quiero reservar una limpieza dental para mañana")]
    public async Task Sprint4_turn_cost_records_repository_calls_without_claiming_external_latency(string message)
    {
        var f = Create(intelligentCommerce: true);
        await f.HandleTurn(message, "cost");
        output.WriteLine("Synthetic handler: Firestore read RPC={0}; commits={1}; locations={2}; offering lists={3}; offering by ID={4}; profiles={5}; hours={6}; engine={7}; entitlements={8}; AI={9}; outbound={10}",
            f.Firestore.ReadRpcCalls, f.Firestore.CommitRpcCalls, f.LocationReads.Invocations.Count,
            f.OfferingReads.Invocations.Count(i => i.Method.Name is "GetServicesAsync" or "GetProductsAsync"),
            f.OfferingReads.Invocations.Count(i => i.Method.Name is "GetServiceByIdAsync" or "GetProductByIdAsync"),
            f.ProfileReads.Invocations.Count, f.HoursReads.Invocations.Count, f.EngineReads.Invocations.Count,
            f.EntitlementReads.Invocations.Count, f.AiCalls,
            f.Outbound.Invocations.Count(i => i.Method.Name is "SendMessageAsync" or "SendDocumentAsync"));
        Assert.Equal(0, f.AiCalls); Assert.Single(f.Sent); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Sprint4_referential_price_reuses_one_current_service_without_scanning_the_catalog()
    {
        var f = Create(); await Ready(f);
        f.Services[0].PriceMinorUnits = 3750;
        f.OfferingReads.Invocations.Clear();
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("Antes dime cuánto cuesta", "price-cost");
        Assert.Contains("37.50", f.Sent["price-cost"].Content);
        Assert.Contains(f.Services[0].Name, f.Sent["price-cost"].Content);
        Assert.DoesNotContain(f.Services[1].Name, f.Sent["price-cost"].Content);
        f.OfferingReads.Verify(o => o.GetServicesAsync(f.Workspace, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        f.OfferingReads.Verify(o => o.GetServiceByIdAsync(f.Workspace, "service-a", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("different-location")]
    [InlineData("deleted-location")]
    [InlineData("revoked")]
    public async Task Sprint4_point_price_keeps_current_location_activity_and_license_filters(string change)
    {
        var f = Create(); await Ready(f); f.Services[0].PriceMinorUnits = 98765;
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        if (change == "inactive") f.Services[0].IsActive = false;
        if (change == "different-location") { f.Services[0].LocationScope = "SPECIFIC"; f.Services[0].LocationIds = ["sede-b"]; }
        if (change == "deleted-location") f.LocationReads.Setup(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<LocationDto>());
        if (change == "revoked") f.Modules.Remove("SERVICES");
        f.OfferingReads.Invocations.Clear();
        await f.HandleTurn("Antes dime cuánto cuesta", "restricted-price");
        Assert.DoesNotContain("987.65", f.Sent["restricted-price"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
        if (change == "revoked") Assert.Empty(f.OfferingReads.Invocations);
    }

    [Fact]
    public async Task Sprint4_price_replay_does_not_repeat_reads_and_revocation_before_delivery_suppresses_facts()
    {
        var f = Create(); await Ready(f); f.Services[0].PriceMinorUnits = 98765;
        f.DuringComposition = () => { f.Modules.Remove("SERVICES"); return Task.CompletedTask; };
        await f.HandleTurn("Antes dime cuánto cuesta", "price-replay");
        Assert.DoesNotContain("987.65", f.Sent["price-replay"].Content);
        var reads = f.OfferingReads.Invocations.Count;
        await f.HandleTurn("Antes dime cuánto cuesta", "price-replay");
        Assert.Equal(reads, f.OfferingReads.Invocations.Count); Assert.Single(f.Sent);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Sprint4_takeover_during_point_price_composition_prevents_automatic_delivery()
    {
        var f = Create(); await Ready(f);
        f.DuringComposition = async () => {
            var current = await f.Context(); current.Mode = "Human";
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, current, default);
        };
        await Assert.ThrowsAsync<NexFlow.Domain.Exceptions.ConcurrencyException>(() => f.HandleTurn("Antes dime cuánto cuesta", "price-human"));
        Assert.Empty(f.Sent); Assert.Empty(f.Bookings); Assert.Equal("Human", (await f.Context()).Mode);
    }

    [Fact]
    public async Task Sprint4_menu_reuses_locations_only_within_its_own_construction()
    {
        var f = Create();
        f.HoursReads.Setup(h => h.GetBusinessHoursAsync(f.Workspace, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BusinessHoursDto>());
        await f.HandleTurn("menú", "menu-cost");
        f.LocationReads.Verify(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("Consultar ubicaciones", f.Sent["menu-cost"].Content);
        await f.HandleTurn("menú", "next-menu");
        f.LocationReads.Verify(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sprint4_public_address_without_ai_still_requires_locations_and_keeps_the_draft(bool draft)
    {
        var f = Create(); if (draft) await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Modules.Remove("LOCATIONS");
        await f.HandleTurn("¿Cuál es la dirección del negocio?", "denied-address");
        Assert.Empty(f.LocationReads.Invocations); Assert.Empty(f.KnowledgeReads.Invocations);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.DoesNotContain("Dirección verificada", f.Sent["denied-address"].Content);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("time")]
    [InlineData("service")]
    [InlineData("location")]
    [InlineData("preference")]
    public async Task Sprint4_old_numeric_options_are_invalid_after_the_draft_scope_changes(string field)
    {
        var f = Create(); await Ready(f); var context = await f.Context();
        context.CurrentStep = "COLLECT_TIME"; context.TargetTime = null;
        await f.Recovery.DecorateAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, context, f.Modules, "Horarios disponibles", default);
        Assert.NotEmpty(context.RecoveryOptions);
        switch (field)
        {
            case "date": context.TargetDate = "2026-10-23"; break;
            case "time": context.TargetTime = "16:00"; break;
            case "service": context.SelectedServiceId = "service-b"; break;
            case "location": context.SelectedLocationId = "sede-b"; break;
            case "preference": context.PreferredTime = new(TimePreferenceKind.After, From: new(8, 0)); break;
        }
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, context, default);
        var decision = await f.Recovery.RecoverAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, "chat",
            new("instance", AssistantConversationTests.Fixture.Phone, "Ana", "1", "stale-option", false, f.Workspace), context, f.Modules, default);
        Assert.Null(decision!.Interpretation); Assert.Contains("lista actual", decision.Response);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData("No te pregunté por las direcciones", "Horario general")]
    [InlineData("Te pregunté por los horarios", "Horario general")]
    [InlineData("No era eso", "Estábamos consultando los horarios")]
    public async Task Contextual_repairs_preserve_the_factual_topic_without_ai_or_transactions(string correction, string expected)
    {
        var f = Create(); await f.HandleTurn("cuáles son sus horarios", "hours");
        await f.HandleTurn(correction, "repair");
        Assert.Contains(expected, f.Sent["repair"].Content); Assert.DoesNotContain("Dirección", f.Sent["repair"].Content);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Corrected_offering_type_does_not_choose_the_negated_type_or_mutate_a_draft()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("No, hablo de servicios, no de productos", "correct-type");
        Assert.Contains(f.Services[0].Name, f.Sent["correct-type"].Content);
        Assert.Equal(nameof(KnowledgeTopic.Services), (await f.Context()).Information!.Topic);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Other_location_is_resolved_only_when_one_authorized_alternative_remains()
    {
        var f = Create(); await f.HandleTurn("qué horarios tienen en Concepción", "first");
        await f.HandleTurn("Me refiero a la otra sede", "other");
        Assert.Contains("Sede San Jeronimo", f.Sent["other"].Content);
        Assert.Equal("sede-b", (await f.Context()).Information!.LocationId);
        Assert.Null((await f.Context()).SelectedLocationId); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Price_interruption_and_natural_resume_keep_verified_booking_slots_without_confirmation()
    {
        var f = Create(); await Ready(f); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("Antes dime cuánto cuesta", "price");
        Assert.Contains(f.Services[0].Name, f.Sent["price"].Content); Assert.Empty(f.Bookings);
        await f.HandleTurn("Perfecto, sigamos", "resume");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Contains("confirm", f.Sent["resume"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_conditional_yes_edits_time_but_never_commits_the_old_summary()
    {
        var f = Create(); await Ready(f);
        var before = await f.Context();
        await f.HandleTurn("Sí, pero quiero otra hora", "correction");
        var state = await f.Context(); Assert.Equal("COLLECT_TIME", state.CurrentStep);
        Assert.Equal(before.SelectedLocationId, state.SelectedLocationId); Assert.Equal(before.SelectedServiceId, state.SelectedServiceId);
        Assert.Equal(before.TargetDate, state.TargetDate); Assert.Equal(before.RealCustomerName, state.RealCustomerName);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Date_and_period_correction_returns_real_slots_without_inventing_an_exact_hour()
    {
        var f = Create(); await Ready(f);
        await f.HandleTurn("Mejor el martes que viene por la tarde", "preference");
        var state = await f.Context(); Assert.Equal("2026-10-27", state.TargetDate); Assert.Null(state.TargetTime);
        Assert.NotNull(state.PreferredTime); Assert.Contains("16:00", f.Sent["preference"].Content);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Asking_about_confirmation_does_not_confirm_and_readonly_decline_preserves_the_draft()
    {
        var f = Create(); await Ready(f); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("¿Ya quedó confirmada?", "question"); Assert.Empty(f.Bookings);
        await f.HandleTurn("No quiero reservar, solo estoy preguntando", "information-only");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Null((await f.Context()).PendingRecoveryAction); Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Order_information_resume_keeps_items_and_does_not_submit()
    {
        var f = Create(); await f.SeedDraft("ORDER");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("cuáles son sus horarios", "info"); await f.HandleTurn("Está bien, continuemos", "resume");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Orders); Assert.Equal(0, f.AiCalls);
    }

    private static AssistantConversationTests.Fixture Create(bool intelligentCommerce = false)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: intelligentCommerce) { Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc) };
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
        Assert.Contains("Estábamos consultando la información del negocio", f.Sent["repair"].Content);
        await f.HandleTurn("Yo pregunté por sus horarios", "correct-topic");
        Assert.Contains("Horario de atención de Sede Concepcion", f.Sent["correct-topic"].Content);
        await f.HandleTurn("Eso ya te lo dije", "repeat");
        Assert.Contains("Estábamos consultando los horarios", f.Sent["repeat"].Content);
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
