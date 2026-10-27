using System.Text.Json;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using Xunit;
using Xunit.Abstractions;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;

namespace NexFlow.Tests.Concierge;

// Real handler -> real orchestrator -> real pipeline -> real booking flow.
// Providers/engine/outbound transport are simulated; context uses the real Firestore repository + in-memory RPC.
public sealed class DialogueIntegrationTests(ITestOutputHelper output)
{
    private static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true)
    { Now = new DateTime(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc) };

    [Theory]
    [InlineData("sí")]
    [InlineData("De acuerdo")]
    public async Task Runtime_invitation_yes_prepares_only_the_invited_reservation_without_ai_or_effects(string answer)
    {
        var f = Create();
        await f.HandleTurn("No sé cómo hacer una reserva", "invitation");
        var question = (await f.Context()).CurrentQuestion!;
        Assert.Equal(ExpectedReply.StartReservation, question.Expected);
        var restored = JsonSerializer.Deserialize<ConversationContextDto>(JsonSerializer.Serialize(await f.Context()))!;
        Assert.Equal(question, ConversationQuestion.Current(f.Workspace, restored, f.Now));
        await f.HandleTurn(answer, "accept-invitation");
        Assert.Equal("RESERVATION", (await f.Context()).CurrentGoal);
        Assert.Equal("COLLECT_LOCATION", (await f.Context()).CurrentStep);
        Assert.Contains("sede", f.Sent["accept-invitation"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders); Assert.Equal(0, f.AiCalls);
        await f.HandleTurn(answer, "accept-invitation");
        Assert.Equal(2, f.Sent.Count);
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("expiry")]
    [InlineData("draft")]
    [InlineData("options")]
    public async Task Runtime_invalid_reference_cannot_select_or_start_an_operation(string invalidation)
    {
        var f = Create(); await f.HandleTurn("menú", "menu");
        var state = await f.Context();
        state.CurrentQuestion = invalidation switch {
            "workspace" => state.CurrentQuestion! with { WorkspaceId = Guid.NewGuid() },
            "expiry" => state.CurrentQuestion! with { ExpiresAt = f.Now },
            _ => state.CurrentQuestion
        };
        if (invalidation == "draft") state.TargetDate = "2026-10-24";
        if (invalidation == "options") state.RecoveryOptions.Reverse();
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        var before = Draft(state);
        await f.HandleTurn("sí", "stale-yes");
        Assert.Equal(before, Draft(await f.Context()));
        Assert.Contains("ya no está vigente", f.Sent["stale-yes"].Content);
        Assert.Empty(f.FactualQueries); Assert.Empty(f.Bookings); Assert.Empty(f.Orders); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Runtime_invitation_rechecks_license_and_repair_preserves_data_already_supplied()
    {
        var f = Create(); await f.HandleTurn("No sé cómo hacer una reserva", "invite");
        f.Modules.Remove("RESERVATIONS"); f.OfferingReads.Invocations.Clear();
        await f.HandleTurn("sí", "revoked-invite");
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.OfferingReads.Invocations);
        Assert.Contains("no tiene habilitadas las reservas", f.Sent["revoked-invite"].Content);
        f.Modules.Add("RESERVATIONS"); await f.SeedDraft("RESERVATION");
        var before = Draft(await f.Context());
        await f.HandleTurn("ya te lo dije", "repair-known-data");
        Assert.Equal(before, Draft(await f.Context())); Assert.Contains("fecha", f.Sent["repair-known-data"].Content);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Runtime_old_menu_cannot_override_a_new_invitation_even_if_the_draft_is_unchanged()
    {
        var f = Create();
        await f.HandleTurn("menú", "old-menu");
        await f.HandleTurn("No sé cómo hacer una reserva", "new-question");
        await f.HandleTurn("1", "old-choice");
        Assert.Empty(f.FactualQueries);
        Assert.Null((await f.Context()).CurrentGoal);
        Assert.Contains("vigente", f.Sent["old-choice"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Runtime_old_menu_is_not_rebound_as_a_new_date_question()
    {
        var f = Create(); await f.HandleTurn("menú", "old-list");
        await f.HandleTurn("Quiero reservar limpieza dental en Concepción", "date-question");
        var state = await f.Context();
        Assert.Equal("COLLECT_DATE", state.CurrentStep);
        Assert.Equal(ExpectedReply.RequestedData, state.CurrentQuestion!.Expected);
        var before = Draft(state);
        await f.HandleTurn("1", "obsolete-number");
        Assert.Equal(before, Draft(await f.Context())); Assert.Empty(f.FactualQueries);
        Assert.Contains("pregunta vigente", f.Sent["obsolete-number"].Content);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Runtime_yes_after_an_information_question_never_confirms_the_previous_summary()
    {
        var f = Create(); await Ready(f);
        await f.HandleTurn("Antes dime cuánto cuesta", "price");
        var before = Draft(await f.Context());
        await f.HandleTurn("sí", "informational-yes");
        Assert.Empty(f.Bookings); Assert.Equal(before, Draft(await f.Context()));
        await f.HandleTurn("continuemos", "resume-summary");
        Assert.Contains("Reserva por confirmar", f.Sent["resume-summary"].Content);
        await f.HandleTurn("sí confirmo", "current-consent");
        await f.HandleTurn("sí confirmo", "current-consent");
        Assert.Single(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Runtime_what_repeats_the_current_availability_reference_without_reset_or_ai()
    {
        var f = Create(); await Ready(f);
        await f.HandleTurn("¿Hay a las 4 de la tarde?", "availability");
        var before = Draft(await f.Context()); var calls = f.AiCalls;
        await f.HandleTurn("¿Qué?", "reference-repair");
        Assert.Contains("16:00", f.Sent["reference-repair"].Content);
        Assert.Equal(before, Draft(await f.Context())); Assert.Equal(calls, f.AiCalls);
        Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Runtime_repeating_the_current_summary_keeps_its_consent_reference()
    {
        var f = Create(); await Ready(f);
        await f.HandleTurn("continuemos", "summary");
        await f.HandleTurn("¿Qué?", "repeat-summary");
        Assert.Contains("Reserva por confirmar", f.Sent["repeat-summary"].Content);
        Assert.Empty(f.Bookings);
        await f.HandleTurn("sí confirmo", "consent-after-repeat");
        Assert.Single(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Runtime_legacy_confirmation_revalidates_the_summary_before_accepting_consent()
    {
        var f = Create(); await Ready(f);
        Assert.Null((await f.Context()).CurrentQuestion);
        await f.HandleTurn("sí confirmo", "legacy-answer");
        Assert.Empty(f.Bookings);
        Assert.Contains("Reserva por confirmar", f.Sent["legacy-answer"].Content);
        Assert.Equal(ExpectedReply.ConfirmReservation, (await f.Context()).CurrentQuestion!.Expected);
        await f.HandleTurn("sí confirmo", "fresh-consent");
        Assert.Single(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Runtime_question_binding_preserves_the_existing_order_continuation_contract()
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true);
        await f.HandleTurn("Quiero dos panes integrales", "order-draft");
        var state = await f.Context(); var question = state.LastQuestion;
        Assert.Contains("¿Deseas agregar o cambiar", question); Assert.DoesNotContain("5.00", question);
        var before = Draft(state);
        await f.HandleTurn("¿Qué horarios tienen?", "order-info");
        Assert.Equal(question, (await f.Context()).LastQuestion);
        Assert.Equal(before, Draft(await f.Context())); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multi_slot_start_keeps_afternoon_as_a_preference_and_commits_once_only_after_explicit_confirmation(bool reservationOnly)
    {
        var f = Create(); if (reservationOnly) f.Modules.IntersectWith(["SERVICES", "RESERVATIONS"]);
        await f.HandleTurn("Soy Ana Pérez, quiero limpieza dental en Concepción pasado mañana por la tarde", "start");
        var state = await f.Context();
        Assert.Equal("RESERVATION", state.CurrentGoal); Assert.Equal("Ana Pérez", state.RealCustomerName);
        Assert.Equal("sede-a", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("2026-10-22", state.TargetDate); Assert.Null(state.TargetTime);
        Assert.Equal(TimePreferenceKind.Period, state.PreferredTime!.Kind);
        Assert.Contains("hora", f.Sent["start"].Content); Assert.DoesNotContain("16:00", f.Sent["start"].Content);
        await f.HandleTurn("muéstrame opciones", "explicit-options");
        Assert.Contains("16:00", f.Sent["explicit-options"].Content); Assert.DoesNotContain("10:00", f.Sent["explicit-options"].Content);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls); Cost(f, "multi-slot");
        await f.HandleTurn("16:00", "time"); Assert.Empty(f.Bookings);
        Assert.Equal("CONFIRM", (await f.Context()).CurrentStep); Assert.Contains("confirm", f.Sent["time"].Content, StringComparison.OrdinalIgnoreCase);
        await f.HandleTurn("sí", "confirm"); await f.HandleTurn("sí", "confirm");
        Assert.Single(f.Bookings); Assert.Equal(f.Workspace, Assert.Single(f.Bookings.Values).WorkspaceId);
        Assert.Null((await f.Context()).CurrentGoal);
    }

    [Fact]
    public async Task Names_accents_and_pending_service_are_used_through_the_live_handler_without_ai()
    {
        var f = Create();
        await f.HandleTurn("quiero reservar limpieza dental pasado mañana", "start");
        Assert.Equal("service-a", Assert.Single((await f.Context()).PendingServiceIds));
        Assert.Null((await f.Context()).SelectedServiceId);
        f.OfferingReads.Invocations.Clear(); f.ProfileReads.Invocations.Clear(); f.LocationReads.Invocations.Clear();
        await f.HandleTurn("Soy Deyvid Pariona", "name");
        Assert.Equal("Deyvid Pariona", (await f.Context()).RealCustomerName);
        Assert.Contains("sede", f.Sent["name"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, CatalogCalls(f, "GetServicesAsync")); Assert.Equal(0, CatalogCalls(f, "GetProductsAsync"));
        Cost(f, "name reply (delta)");
        f.OfferingReads.Invocations.Clear(); f.ProfileReads.Invocations.Clear(); f.LocationReads.Invocations.Clear();
        await f.HandleTurn("En la sede concepción", "location");
        var state = await f.Context(); Assert.Equal("sede-a", state.SelectedLocationId);
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Empty(state.PendingServiceIds);
        Assert.Equal("2026-10-22", state.TargetDate); Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
        Cost(f, "accented location reply (delta)");
    }

    [Theory]
    [InlineData("pasado mañana", "2026-10-22")]
    [InlineData("el 22 de octubre", "2026-10-22")]
    [InlineData("22/10", "2026-10-22")]
    public async Task Requested_dates_use_business_time_and_do_not_spend_ai(string text, string expected)
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        await f.HandleTurn(text, "date");
        Assert.Equal(expected, (await f.Context()).TargetDate); Assert.Equal("COLLECT_TIME", (await f.Context()).CurrentStep);
        Assert.Contains("22/10/2026", f.Sent["date"].Content); Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
        Assert.Equal(0, CatalogCalls(f, "GetServicesAsync")); Assert.Equal(0, CatalogCalls(f, "GetProductsAsync"));
        Cost(f, $"date reply ({text})");
    }

    [Theory]
    [InlineData("que horario tienen", ConversationIntent.BusinessHours)]
    [InlineData("cuánto cuesta la limpieza dental", ConversationIntent.ServiceQuery)]
    public async Task Informational_interruptions_answer_real_facts_without_mutating_any_draft_slot(string text, ConversationIntent intent)
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var before = Draft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = intent, SearchTerm = "Limpieza dental", QueryKind = OfferingQueryKind.Specific });
        await f.HandleTurn(text, "info");
        Assert.Contains("Dato real autorizado", f.Sent["info"].Content); Assert.Contains("fecha", f.Sent["info"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Draft(await f.Context())); Assert.Single(f.FactualQueries); Assert.Empty(f.Bookings);
        Cost(f, "information interruption");
    }

    [Fact]
    public async Task Changing_location_revalidates_and_preserves_compatible_service_time_name_and_date()
    {
        var f = Create(); await Ready(f);
        await f.HandleTurn("mejor sede San Jerónimo", "move");
        var state = await f.Context(); Assert.Equal("sede-b", state.SelectedLocationId);
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Equal("16:00", state.TargetTime); Assert.Null(state.PreferredTime);
        Assert.Equal("2026-10-22", state.TargetDate); Assert.Equal("Ana Pérez", state.RealCustomerName);
        Assert.Contains("servicio", f.Sent["move"].Content, StringComparison.OrdinalIgnoreCase); Assert.Empty(f.Bookings);
        Cost(f, "location correction");
    }

    [Fact]
    public async Task Correcting_date_revalidates_the_selected_time_and_does_not_commit()
    {
        var f = Create(); await Ready(f);
        await f.HandleTurn("mejor el 23 de octubre", "date-correction");
        var state = await f.Context(); Assert.Equal("2026-10-23", state.TargetDate); Assert.Equal("16:00", state.TargetTime);
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Equal("CONFIRM", state.CurrentStep);
        Assert.Contains("23/10/2026", f.Sent["date-correction"].Content); Assert.Empty(f.Bookings);
        Cost(f, "date correction");
    }

    [Fact]
    public async Task Ambiguous_service_requests_precision_without_arbitrarily_selecting_or_committing()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var state = await f.Context(); state.SelectedServiceId = null; state.CurrentStep = "COLLECT_SERVICE";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("limpieza", "ambiguous");
        state = await f.Context(); Assert.Null(state.SelectedServiceId); Assert.Equal(2, state.PendingServiceIds.Count);
        Assert.Contains("Limpieza dental", f.Sent["ambiguous"].Content); Assert.Contains("Limpieza profunda", f.Sent["ambiguous"].Content);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
        await f.HandleTurn("limpieza profunda", "choose");
        Assert.Equal("service-b", (await f.Context()).SelectedServiceId); Assert.Empty((await f.Context()).PendingDialogueSlots);
        Cost(f, "ambiguous service + resolved choice");
    }

    [Fact]
    public async Task Provider_outages_never_consume_semantic_clarifications_or_force_handoff_and_local_data_can_resume()
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var before = Draft(await f.Context());
        for (var i = 0; i < 3; i++)
        {
            f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProviderUnavailable, Failure = NexFlow.Application.Engines.AI.AiFailureKind.Unavailable });
            await f.HandleTurn("qué alternativas podemos conversar", "outage-" + i);
            Assert.Contains("fecha", f.Sent["outage-" + i].Content, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(3, f.AiCalls); Assert.Equal(0, f.Handoffs); Assert.Equal(0, (await f.Context()).ClarificationAttempts);
        Assert.Equal(before, Draft(await f.Context()));
        Cost(f, "three provider outages");
        await f.HandleTurn("pasado mañana", "resume"); Assert.Equal("2026-10-22", (await f.Context()).TargetDate);
        Assert.Equal(3, f.AiCalls); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Revoked_license_cannot_change_or_commit_an_existing_draft()
    {
        var f = Create(); await Ready(f); var before = Draft(await f.Context()); f.Modules.Remove("RESERVATIONS");
        f.OfferingReads.Invocations.Clear();
        await f.HandleTurn("pasado mañana", "revoked");
        Assert.Contains("no tiene habilitadas las reservas", f.Sent["revoked"].Content);
        Assert.Equal(before, Draft(await f.Context())); Assert.Empty(f.Bookings);
        Assert.Equal(0, CatalogCalls(f, "GetServicesAsync")); Assert.Equal(0, CatalogCalls(f, "GetProductsAsync"));
        Cost(f, "revoked reservation license");
    }

    [Fact]
    public async Task Explicit_handoff_is_acknowledged_once_and_suppresses_later_automatic_responses()
    {
        var f = Create(); await f.HandleTurn("asesor", "handoff"); await f.HandleTurn("asesor", "handoff");
        Assert.Equal(1, f.Handoffs); Assert.Single(f.Sent); Assert.Equal("Human", (await f.Context()).Mode);
        await f.HandleTurn("quiero reservar limpieza dental", "after-human");
        Assert.Single(f.Sent); Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
        Cost(f, "explicit human handoff");
    }

    [Fact]
    public async Task Instruction_is_not_an_operation_and_yes_before_confirmation_cannot_create_a_reservation()
    {
        var f = Create(); await f.HandleTurn("No sé cómo hacer una reserva", "how");
        Assert.Contains("Para reservar", f.Sent["how"].Content); Assert.Null((await f.Context()).CurrentGoal);
        Assert.Equal(0, f.AiCalls); Assert.Equal(0, CatalogCalls(f, "GetServicesAsync"));
        Cost(f, "reservation instructions");
        await f.HandleTurn("sí", "premature-yes"); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Stale_ai_turn_cannot_overwrite_a_newer_context_or_trigger_an_operation()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Date = "2026-10-22" });
        f.DuringInterpretation = async () => { var newer = await f.Context(); newer.TargetDate = "2026-10-24"; await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, newer, default); };
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.HandleTurn("qué más podemos hacer", "stale"));
        Assert.Equal("2026-10-24", (await f.Context()).TargetDate); Assert.Empty(f.Sent); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Unknown_delivery_and_duplicate_sources_are_not_resent_or_applied_twice()
    {
        var f = Create(); f.DeliveryStatus = MessageStatus.UnknownDelivery;
        await f.HandleTurn("quiero reservar limpieza dental", "uncertain");
        var before = JsonSerializer.Serialize(await f.Context());
        await f.HandleTurn("quiero reservar limpieza dental", "uncertain");
        Assert.Single(f.Sent); Assert.Equal(MessageStatus.UnknownDelivery, f.Sent["uncertain"].Status);
        Assert.Equal(before, JsonSerializer.Serialize(await f.Context())); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Ai_cannot_apply_an_entity_or_date_without_user_evidence_or_workspace_catalog_membership()
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var original = await f.Context();
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Location = "foreign-location", Service = "foreign-service", CustomerName = "Nombre inventado", Date = "2026-11-01", Time = "17:00" });
        await f.HandleTurn("qué más podemos hacer", "untrusted");
        var state = await f.Context();
        Assert.Equal(original.RealCustomerName, state.RealCustomerName); Assert.Equal(original.SelectedLocationId, state.SelectedLocationId);
        Assert.Equal(original.SelectedServiceId, state.SelectedServiceId); Assert.Null(state.TargetDate); Assert.Null(state.TargetTime);
        Assert.NotEmpty(state.PendingDialogueSlots); Assert.Empty(f.Bookings); Assert.Equal(1, f.AiCalls);
    }

    [Fact]
    public async Task A_module_revoked_while_ai_is_running_is_rechecked_before_applying_the_proposal()
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var before = Draft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Service = "Limpieza dental" });
        f.DuringInterpretation = () => { f.Modules.Remove("SERVICES"); f.OfferingReads.Invocations.Clear(); return Task.CompletedTask; };
        await f.HandleTurn("qué más puedo hacer con limpieza dental", "revoked-in-flight");
        Assert.Contains("No hay información de servicios", f.Sent["revoked-in-flight"].Content);
        Assert.Equal(before, Draft(await f.Context())); f.OfferingReads.VerifyNoOtherCalls(); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Contextual_interpreter_reuses_the_authorized_business_clock_without_a_second_profile_read()
    {
        var f = new ConciergeFixture(); var router = new Mock<IAiRouter>(MockBehavior.Strict);
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), "cuánto cuesta la limpieza", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"Intent":"ServiceQuery","SearchTerm":"limpieza"}""");
        var interpreter = new AiInterpreter(router.Object, Mock.Of<IClock>(c => c.UtcNow == f.Now), f.Profiles.Object, NullLogger<AiInterpreter>.Instance);
        var recovery = new Mock<IConversationRecoveryService>(MockBehavior.Strict);
        recovery.Setup(r => r.AcquireAiAsync(f.Workspace, "phone", "contextual", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var pipeline = new TurnUnderstandingPipeline(f.Resolver, new DialoguePolicy(), interpreter, recovery.Object, f.Offerings.Object);
        var result = await pipeline.PrepareAsync(f.Workspace, "phone", new("instance", "phone", "Ana", "cuánto cuesta la limpieza", "contextual", false, f.Workspace),
            new() { CurrentGoal = "RESERVATION", CurrentStep = "COLLECT_DATE" }, f.Modules, default);
        Assert.True(result.UsedAi); Assert.Equal(DialogueAct.InformationalInterruption, result.Act);
        Assert.Null(result.Interpretation.BookingProposal);
        f.Profiles.Verify(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()), Times.Once);
        router.Verify(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Runtime_semantic_projection_has_a_verified_question_but_no_history_customer_or_summary()
    {
        var f = new ConciergeFixture(); var state = new ConversationContextDto {
            CurrentGoal = "RESERVATION", CurrentStep = "COLLECT_DATE", RealCustomerName = "Private Person",
            Information = new() { Topic = "Availability" }
        };
        ConversationQuestion.Remember(f.Workspace, "private-source", state, ExpectedReply.Information,
            ConversationIntent.Reservation, "Private summary with customer and contact", f.Now);
        string? prompt = null;
        var router = new Mock<IAiRouter>(MockBehavior.Strict);
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), "¿Y en relación a eso?", true, It.IsAny<CancellationToken>()))
            .Callback<AiTaskType, string, string, bool, CancellationToken>((_, system, _, _, _) => prompt = system)
            .ReturnsAsync("""{"Intent":"General","RequiresClarification":true}""");
        var interpreter = new AiInterpreter(router.Object, Mock.Of<IClock>(c => c.UtcNow == f.Now), f.Profiles.Object, NullLogger<AiInterpreter>.Instance);
        var turn = DialogueTurnContext.Capture(f.Workspace, "semantic-followup", state, new DateTimeOffset(f.Now),
            TimeZoneInfo.Utc, f.Modules);
        var result = await interpreter.InterpretAsync("¿Y en relación a eso?", turn, default);
        Assert.Equal(ConversationIntent.General, result.Intent); Assert.True(result.RequiresClarification);
        Assert.Contains("\"Expected\":\"Information\"", prompt); Assert.Contains("Availability", prompt);
        Assert.DoesNotContain("Private Person", prompt); Assert.DoesNotContain("Private summary", prompt);
        Assert.DoesNotContain("private-source", prompt); Assert.DoesNotContain(f.Workspace.ToString(), prompt);
        Assert.Contains("Private summary", state.LastQuestion); // Stored history is not used as provider context.
        router.Verify(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
        f.Profiles.VerifyNoOtherCalls();
        state.CurrentQuestion = state.CurrentQuestion! with { ExpiresAt = f.Now };
        Assert.Null(DialogueTurnContext.Capture(f.Workspace, "expired", state, new DateTimeOffset(f.Now), TimeZoneInfo.Utc, f.Modules).Question);
    }

    [Fact]
    public async Task Runtime_semantic_request_about_a_service_never_becomes_a_reservation()
    {
        var f = new ConciergeFixture();
        const string text = "Registra una solicitud sobre limpieza dental";
        var interpreter = new Mock<IAiInterpreter>(MockBehavior.Strict);
        interpreter.Setup(i => i.InterpretAsync(f.Workspace, text, "", null, It.IsAny<HashSet<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiInterpretation { Intent = ConversationIntent.Request, Act = DialogueAct.StartOperation,
                Evidence = [new("Act", text, nameof(DialogueAct.StartOperation))] });
        var recovery = new Mock<IConversationRecoveryService>(MockBehavior.Strict);
        recovery.Setup(r => r.AcquireAiAsync(f.Workspace, "phone", "request", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var pipeline = new TurnUnderstandingPipeline(f.Resolver, new DialoguePolicy(), interpreter.Object, recovery.Object, f.Offerings.Object);
        var result = await pipeline.PrepareAsync(f.Workspace, "phone", new("instance", "phone", "Ana", text, "request", false, f.Workspace),
            new(), f.Modules, default);
        Assert.Equal(ConversationIntent.Request, result.Interpretation.Intent);
        Assert.Null(result.Interpretation.BookingProposal);
        Assert.Equal(DialogueAct.StartOperation, result.Act);
    }

    [Fact]
    public async Task Existing_change_location_directive_and_numeric_menu_preserve_compatible_dependencies()
    {
        var f = Create(); await Ready(f);
        var state = await f.Context(); state.PreferredTime = new(TimePreferenceKind.Period, From: new(12, 0), Until: new(18, 0));
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.ChangeLocation });
        await f.HandleTurn("quiero cambiar de sede", "change-location");
        state = await f.Context(); Assert.Equal("sede-a", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("16:00", state.TargetTime); Assert.NotNull(state.PreferredTime); Assert.Contains("Location", state.PendingDialogueSlots);
        Assert.Contains("sede", f.Sent["change-location"].Content);
        await f.HandleTurn("2", "location-choice");
        state = await f.Context(); Assert.Equal("sede-b", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("16:00", state.TargetTime); Assert.DoesNotContain("Location", state.PendingDialogueSlots);
        Assert.Equal("2026-10-22", state.TargetDate); Assert.Equal("Ana Pérez", state.RealCustomerName); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("qué puedes hacer con mi documento 12345678")]
    [InlineData("qué más podemos hacer")]
    public async Task Unrelated_numbers_do_not_validate_a_date_invented_by_ai(string text)
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Date = "2026-11-01" });
        await f.HandleTurn(text, "invented-date");
        Assert.Null((await f.Context()).TargetDate); Assert.Contains("fecha", f.Sent["invented-date"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_temporarily_unavailable_business_profile_preserves_the_draft_without_ai_or_semantic_penalties()
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var before = Draft(await f.Context());
        f.ProfileReads.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessProfileUnavailableException(true, new TimeoutException()));
        await f.HandleTurn("pasado mañana", "profile-unavailable");
        Assert.Contains("datos del negocio", f.Sent["profile-unavailable"].Content);
        Assert.Equal(before, Draft(await f.Context())); Assert.Equal(0, f.AiCalls);
        Assert.Equal(0, f.Handoffs); Assert.Equal(0, (await f.Context()).ClarificationAttempts); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Revocation_during_profile_loading_prevents_subsequent_sensitive_reads()
    {
        var f = new ConciergeFixture();
        f.Profiles.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { f.Modules.Clear(); return new NexFlow.Application.Features.Business.BusinessProfileDto("Synthetic", "", "", "", ""); });
        var result = await f.Resolver.ResolveAsync(f.Workspace, "revocation", "limpieza en Concepción", new(), SlotResolutionPurpose.Reservation, default);
        Assert.Equal("ModuleDenied", result.Location.Code); Assert.Equal("ModuleDenied", result.Service.Code);
        f.Locations.VerifyNoOtherCalls(); f.Offerings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_into_a_business_or_provider_failure()
    {
        var f = new ConciergeFixture(); using var cancellation = new CancellationTokenSource();
        f.Profiles.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((_, token) => { cancellation.Cancel(); return Task.FromCanceled<NexFlow.Application.Features.Business.BusinessProfileDto?>(token); });
        var pipeline = new TurnUnderstandingPipeline(f.Resolver, new DialoguePolicy(), Mock.Of<IAiInterpreter>(), Mock.Of<IConversationRecoveryService>(), f.Offerings.Object);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.PrepareAsync(f.Workspace, "phone",
            new("instance", "phone", "Ana", "quiero reservar", "cancelled", false, f.Workspace), new(), f.Modules, cancellation.Token));
    }

    [Fact]
    public void Legacy_context_json_remains_compatible_with_additive_pending_candidates_and_preference_fields()
    {
        var state = JsonSerializer.Deserialize<ConversationContextDto>("""{"StateVersion":"legacy","CurrentGoal":"RESERVATION","SelectedLocationId":"sede-a"}""")!;
        Assert.Equal("legacy", state.StateVersion); Assert.Equal("sede-a", state.SelectedLocationId);
        Assert.Empty(state.PendingServiceIds); Assert.Empty(state.PendingDialogueSlots); Assert.Null(state.PreferredTime);
        Assert.Null(state.CurrentQuestion);
    }

    private static async Task Ready(AssistantConversationTests.Fixture f)
    {
        await f.SeedDraft("RESERVATION"); var c = await f.Context();
        c.RealCustomerName = "Ana Pérez"; c.TargetDate = "2026-10-22"; c.TargetTime = "16:00"; c.CurrentStep = "CONFIRM";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, c, default);
    }
    private static string Draft(NexFlow.Application.Abstractions.Cache.ConversationContextDto state) => JsonSerializer.Serialize(new
    { Business = AssistantConversationTests.Fixture.BusinessDraft(state), state.PreferredTime, state.PendingServiceIds, state.PendingDialogueSlots });
    private static int CatalogCalls(AssistantConversationTests.Fixture f, string method) => f.OfferingReads.Invocations.Count(i => i.Method.Name == method);
    private void Cost(AssistantConversationTests.Fixture f, string scenario) => output.WriteLine(
        $"{scenario}: profile={f.ProfileReads.Invocations.Count}, locations={f.LocationReads.Invocations.Count}, services={CatalogCalls(f, "GetServicesAsync")}, products={CatalogCalls(f, "GetProductsAsync")}, serviceById={CatalogCalls(f, "GetServiceByIdAsync")}, serviceAtLocation={CatalogCalls(f, "IsServiceAvailableAtLocationAsync")}, ai={f.AiCalls}. Logical repository invocations; not physical Firestore document billing.");
}

