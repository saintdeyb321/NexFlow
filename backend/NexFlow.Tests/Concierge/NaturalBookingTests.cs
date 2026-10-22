using Xunit;
using Xunit.Abstractions;
using Moq;
using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Tests.Concierge;

public sealed class NaturalBookingTests(ITestOutputHelper output)
{
    private static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true)
        { Now = new DateTime(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc) };

    public sealed record Holdout(string Id, string[] Turns, string? Name, string? Date, int Bookings);
    public static IEnumerable<object[]> HoldoutCases()
    {
        using var stream = typeof(NaturalBookingTests).Assembly.GetManifestResourceStream("NexFlow.Tests.Concierge.NaturalBookingHoldout.json")!;
        return JsonSerializer.Deserialize<Holdout[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Select(c => new object[] { c.Id });
    }

    [Theory]
    [MemberData(nameof(HoldoutCases))]
    public async Task Independent_holdout_conversation(string id)
    {
        using var stream = typeof(NaturalBookingTests).Assembly.GetManifestResourceStream("NexFlow.Tests.Concierge.NaturalBookingHoldout.json")!;
        var conversation = JsonSerializer.Deserialize<Holdout[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Single(c => c.Id == id);
        var f = Create();
        foreach (var text in conversation.Turns)
        {
            // Same controlled semantic-provider script as the isolated before probe.
            var state = await f.Context(); var normalized = SpanishTextNormalizer.Normalize(text);
            var name = new NameSlotResolver().Resolve(text, state.CurrentStep == "COLLECT_CUSTOMERNAME");
            f.Interpretations.Clear();
            f.Interpretations.Enqueue(new() {
                Intent = normalized.Contains("precio") ? ConversationIntent.ServiceQuery : ConversationIntent.Reservation,
                SearchTerm = normalized.Contains("precio") ? "Limpieza profunda" : null,
                CustomerName = name.TryGetValue(out var person) ? person : conversation.Name == text ? text : null,
                Location = normalized.Contains("concepcion") ? "Sede Concepcion" : normalized.Contains("san jeronimo") ? "Sede San Jeronimo" : null,
                Service = normalized.Contains("limpieza dental") ? "Limpieza dental" : normalized.Contains("limpieza profunda") ? "Limpieza profunda" : null,
                Date = normalized.Contains("22") || normalized.Contains("jueves") ? "2026-10-22" : null,
                Time = text.Contains("16:00") || normalized.Contains("cuatro de la tarde") ? "16:00" : null });
            await Turn(f, text);
        }
        Assert.Equal(conversation.Bookings, f.Bookings.Count);
        if (conversation.Bookings > 0)
        {
            var booking = Assert.Single(f.Bookings.Values); Assert.Equal(conversation.Name, booking.CustomerName);
            Assert.Equal(conversation.Date, TimeZoneInfo.ConvertTimeFromUtc(booking.StartTime, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")).ToString("yyyy-MM-dd"));
        }
        Assert.Null((await f.Context()).CurrentGoal); Assert.Equal(0, f.AiCalls); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task A_negated_reservation_never_starts_an_operation()
    {
        var f = Create(); await f.HandleTurn("No quiero reservar", "decline");
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Availability_is_offered_before_asking_for_a_name()
    {
        var f = Create();
        await f.HandleTurn("Quiero limpieza dental en Concepción pasado mañana por la tarde", "start");
        Assert.Contains("16:00", f.Sent["start"].Content);
        Assert.DoesNotContain("nombre", f.Sent["start"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("COLLECT_TIME", (await f.Context()).CurrentStep); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_entire_booking_collects_business_choices_before_the_name_and_commits_once()
    {
        var f = Create();
        foreach (var text in new[] { "Quiero reservar", "En Concepción", "Limpieza dental", "pasado mañana", "por la tarde", "16:00" })
            await Turn(f, text);
        Assert.Null((await f.Context()).RealCustomerName); Assert.Equal("COLLECT_CUSTOMERNAME", (await f.Context()).CurrentStep);
        await Turn(f, "Ana Pérez"); Assert.Empty(f.Bookings); Assert.Contains("Sede Concepcion", f.Sent.Values.Last().Content);
        await Turn(f, "sí", "confirm"); await Turn(f, "sí", "confirm");
        Assert.Single(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task B_C_data_can_arrive_together_and_name_can_be_supplied_only_at_the_end(bool earlyName)
    {
        var f = Create();
        await Turn(f, (earlyName ? "Soy Ana Pérez, " : "") + "quiero limpieza dental en Concepción pasado mañana por la tarde");
        Assert.Equal("2026-10-22", (await f.Context()).TargetDate); Assert.Equal("service-a", (await f.Context()).SelectedServiceId);
        Assert.Null((await f.Context()).TargetTime); Assert.Empty(f.Bookings);
        var plan = f.Plans.Last();
        Assert.Contains(plan.Facts, fact => fact.Key == "AvailableStartUtc");
        Assert.All(plan.Facts.Where(fact => fact.Key == "AvailableStartUtc"), fact => Assert.Equal(VerifiedFactSource.TransactionEngine, fact.Source));
        Assert.Single(plan.Choices); Assert.Equal("16:00", plan.Choices[0].Key);
        await Turn(f, "16:00"); Assert.Empty(f.Bookings);
        if (!earlyName) await Turn(f, "Ana Pérez");
        Assert.Equal("CONFIRM", (await f.Context()).CurrentStep);
        await Turn(f, "sí"); Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task D_price_interruption_and_resume_preserve_validated_fields()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción pasado mañana por la tarde");
        f.Services[0].PriceMinorUnits = 2500;
        f.OfferingReads.Invocations.Clear();
        var before = Draft(await f.Context());
        await Turn(f, "¿Cuánto cuesta?");
        Assert.Equal(before, Draft(await f.Context())); Assert.Contains($"{f.Services[0].Currency} 25.00", f.Sent.Values.Last().Content);
        // Sprint 4 reuses the current point read instead of querying the full type
        // through KnowledgeService (whose legacy fixture returned a fixed S/25).
        f.KnowledgeReads.Verify(k => k.QueryAsync(f.Workspace, It.Is<BusinessKnowledgeSnapshot>(s => s.WorkspaceId == f.Workspace),
            It.Is<KnowledgeQuery>(q => q.Topic == KnowledgeTopic.Services), It.IsAny<CancellationToken>()), Times.Never);
        f.OfferingReads.Verify(o => o.GetServicesAsync(f.Workspace, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        // The second point read belongs to continuation/availability validation;
        // keep that safety barrier rather than treating it as reusable price data.
        f.OfferingReads.Verify(o => o.GetServiceByIdAsync(f.Workspace, "service-a", It.IsAny<CancellationToken>()), Times.Exactly(2));
        await Turn(f, "Está bien, sigamos"); Assert.Equal(before, Draft(await f.Context()));
        Assert.DoesNotContain("proveedor", f.Sent.Values.Last().Content, StringComparison.OrdinalIgnoreCase);
        await Turn(f, "16:00"); await Turn(f, "Ana Pérez"); await Turn(f, "sí"); Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task E_location_change_invalidates_service_and_time_until_revalidated()
    {
        var f = Create(); await Turn(f, "Soy Ana Pérez, quiero limpieza dental en Concepción pasado mañana"); await Turn(f, "16:00");
        await Turn(f, "Mejor San Jerónimo"); var state = await f.Context();
        Assert.Equal("sede-b", state.SelectedLocationId); Assert.Null(state.SelectedServiceId); Assert.Null(state.TargetTime);
        Assert.Equal("Ana Pérez", state.RealCustomerName); Assert.Equal("2026-10-22", state.TargetDate);
        await Turn(f, "Limpieza dental"); await Turn(f, "por la tarde"); await Turn(f, "16:00"); await Turn(f, "sí");
        Assert.Equal("sede-b", Assert.Single(f.Bookings.Values).LocationId);
    }

    [Theory]
    [InlineData("dentro de dos semanas", "2026-11-03")]
    [InlineData("el día posterior a mañana", "2026-10-22")]
    [InlineData("el veintidós de este mes", "2026-10-22")]
    public async Task F_ai_evidence_supports_normalized_dates_without_inventing_them(string fragment, string expected)
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Date = expected, Evidence = [new("Date", fragment, expected)] });
        await Turn(f, "Me conviene " + fragment);
        Assert.Equal(expected, (await f.Context()).TargetDate); Assert.Equal(1, f.AiCalls); Assert.Empty(f.Bookings);
        await Turn(f, "16:00"); await Turn(f, "Ana Pérez"); await Turn(f, "sí"); Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task F_time_evidence_is_checked_against_meaning_and_real_engine_slots()
    {
        var f = Create();
        f.EngineReads.Setup(e => e.GetAvailabilityAsync(f.Workspace, "sede-a", "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, string _, DateTime d, CancellationToken _) => new[] {
                new TimeSlotDto(DateTime.SpecifyKind(d.AddHours(21).AddMinutes(45), DateTimeKind.Utc), DateTime.SpecifyKind(d.AddHours(22).AddMinutes(15), DateTimeKind.Utc), true) });
        await Turn(f, "quiero limpieza dental en Concepción pasado mañana");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Time = "16:45", Evidence = [new("Time", "un cuarto para las cinco de la tarde", "16:45")] });
        await Turn(f, "Prefiero un cuarto para las cinco de la tarde");
        Assert.Equal("16:45", (await f.Context()).TargetTime); Assert.Equal("COLLECT_CUSTOMERNAME", (await f.Context()).CurrentStep);
        await Turn(f, "Ana Pérez"); await Turn(f, "sí"); Assert.Single(f.Bookings);
    }

    [Theory]
    [InlineData("No quiero reservar")]
    [InlineData("Quiero información sobre reservas")]
    [InlineData("No sé cómo reservar")]
    [InlineData("¿Puedo reservar?")]
    public async Task G_negations_and_instructional_questions_are_not_start_commands(string text)
    {
        var f = Create(); await Turn(f, text); Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task G_cancellation_is_confirmed_without_creating_or_removing_a_real_booking()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción pasado mañana"); var before = Draft(await f.Context());
        await Turn(f, "Ya no quiero continuar"); Assert.Equal(before, Draft(await f.Context()));
        Assert.Equal("CANCEL", (await f.Context()).PendingRecoveryAction);
        await Turn(f, "sí"); Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task H_ambiguous_service_is_never_selected_arbitrarily()
    {
        var f = Create(); await Turn(f, "quiero reservar en Concepción"); await Turn(f, "limpieza");
        Assert.Null((await f.Context()).SelectedServiceId); Assert.Contains("Limpieza dental", f.Sent.Values.Last().Content);
        Assert.Contains("Limpieza profunda", f.Sent.Values.Last().Content);
        await Turn(f, "Limpieza profunda"); await Turn(f, "pasado mañana"); await Turn(f, "16:00"); await Turn(f, "Ana Pérez"); await Turn(f, "sí");
        Assert.Equal("service-b", Assert.Single(f.Bookings.Values).ServiceId);
    }

    [Fact]
    public async Task I_three_provider_failures_keep_a_concrete_next_question_and_do_not_escalate()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción pasado mañana"); var before = Draft(await f.Context());
        for (var i = 0; i < 3; i++)
        {
            f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProviderUnavailable, Failure = NexFlow.Application.Engines.AI.AiFailureKind.Timeout });
            await Turn(f, "¿Qué me recomiendas para seguir?");
            var response = f.Sent.Values.Last().Content;
            Assert.DoesNotContain("IA", response, StringComparison.Ordinal); Assert.DoesNotContain("borrador", response, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("horario", response, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(before, Draft(await f.Context())); Assert.Equal(0, f.Handoffs); Assert.Equal(0, (await f.Context()).ClarificationAttempts);
        await Turn(f, "16:00"); await Turn(f, "Ana Pérez"); await Turn(f, "sí"); Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task J_revocation_stops_progress_and_restoring_permission_allows_a_new_inbound_to_resume()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción pasado mañana"); var before = Draft(await f.Context());
        f.Modules.Remove("RESERVATIONS"); await Turn(f, "16:00"); Assert.Equal(before, Draft(await f.Context())); Assert.Empty(f.Bookings);
        f.Modules.Add("RESERVATIONS"); await Turn(f, "16:00"); await Turn(f, "Ana Pérez"); await Turn(f, "sí"); Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task K_changing_to_an_order_requires_resolving_the_existing_reservation_first()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción pasado mañana"); var before = Draft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, SearchTerm = "1x Producto real" });
        await Turn(f, "Ahora quiero comprar un producto"); Assert.Equal(before, Draft(await f.Context())); Assert.Empty(f.Orders);
        await Turn(f, "¿Cuánto cuesta ese servicio?"); Assert.Equal(before, Draft(await f.Context())); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task L_duplicate_confirmation_and_a_later_yes_cannot_create_a_second_reservation()
    {
        var f = Create(); await Turn(f, "Soy Ana Pérez, quiero limpieza dental en Concepción pasado mañana"); await Turn(f, "16:00");
        await Turn(f, "sí", "commit"); await Turn(f, "sí", "commit"); await Turn(f, "sí", "later-yes"); Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task M_concurrent_messages_use_cas_then_retry_without_losing_the_winning_slots()
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var reads = 0;
        f.ProfileReads.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>())).Returns(async () => {
            if (Interlocked.Increment(ref reads) == 2) gate.TrySetResult();
            await gate.Task; return new BusinessProfileDto("Synthetic", "", "", "", ""); });
        async Task<bool> Attempt(string text, string source) { try { await Turn(f, text, source); return true; } catch (ConcurrencyException) { return false; } }
        var results = await Task.WhenAll(Attempt("Soy Beatriz Lara", "rapid-name"), Attempt("pasado mañana", "rapid-date"));
        Assert.Single(results, r => r);
        if (!results[0]) await Turn(f, "Soy Beatriz Lara", "rapid-name");
        else await Turn(f, "pasado mañana", "rapid-date");
        Assert.Equal("Beatriz Lara", (await f.Context()).RealCustomerName); Assert.Equal("2026-10-22", (await f.Context()).TargetDate); Assert.Empty(f.Bookings);
        output.WriteLine(JsonSerializer.Serialize(new { MetricScope = "WholeConcurrentConversation", RepositoryInvocations = new Dictionary<string, int> {
            ["profile"] = f.ProfileReads.Invocations.Count, ["locations"] = f.LocationReads.Invocations.Count, ["offerings"] = f.OfferingReads.Invocations.Count,
            ["hours"] = f.HoursReads.Invocations.Count, ["entitlements"] = f.EntitlementReads.Invocations.Count,
            ["knowledge"] = f.KnowledgeReads.Invocations.Count, ["engine"] = f.EngineReads.Invocations.Count }, f.AiCalls }));
    }

    [Fact]
    public async Task N_human_takeover_during_understanding_suppresses_the_pending_automatic_reply()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción pasado mañana"); var sent = f.Sent.Count;
        f.DuringInterpretation = async () => { var state = await f.Context(); state.Mode = "Human"; await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default); };
        await Assert.ThrowsAsync<ConcurrencyException>(() => Turn(f, "¿Qué alternativas podemos conversar?"));
        Assert.Equal(sent, f.Sent.Count); await Turn(f, "Quiero hablar con una persona"); Assert.Equal(sent, f.Sent.Count); Assert.Empty(f.Bookings);
    }

    private int turn;
    [Theory]
    [InlineData("dentro de dos días", "2026-10-23", "dentro de dos días", "2026-10-23")]
    [InlineData("dentro de dos días", "2026-10-22", "dentro de tres días", "2026-10-22")]
    [InlineData("dentro de dos días", "2026-10-22", "dentro de dos días", "2026-10-23")]
    [InlineData("dentro de dos días o dentro de tres días", "2026-10-22", "dentro de dos días", "2026-10-22")]
    public async Task Evidence_cannot_invent_normalization_fragments_or_discard_alternatives(string input, string date, string span, string normalized)
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Date = date, Evidence = [new("Date", span, normalized)] });
        await Turn(f, input); Assert.Null((await f.Context()).TargetDate); Assert.Empty(f.Bookings);
        Assert.Contains("fecha", f.Sent.Values.Last().Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Occupied_date_offers_only_verified_alternatives_without_changing_the_requested_date()
    {
        var f = Create();
        f.EngineReads.Setup(e => e.GetAvailabilityAsync(f.Workspace, "sede-a", "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, string _, DateTime d, CancellationToken _) => d.Day == 22 ? [] : new[] {
                new TimeSlotDto(DateTime.SpecifyKind(d.AddHours(21), DateTimeKind.Utc), DateTime.SpecifyKind(d.AddHours(21.5), DateTimeKind.Utc), true) });
        await Turn(f, "quiero limpieza dental en Concepción pasado mañana");
        Assert.Equal("2026-10-22", (await f.Context()).TargetDate); Assert.Null((await f.Context()).TargetTime);
        Assert.Contains("23/10/2026", f.Sent.Values.Last().Content); Assert.Contains("16:00", f.Sent.Values.Last().Content);
        Assert.Empty(f.Bookings); Assert.Contains(f.Plans.Last().Facts, fact => fact.Key == "AvailableStartUtc"); Assert.All(f.Plans.Last().Facts.Where(fact => fact.Key == "AvailableStartUtc"), fact => Assert.Contains("2026-10-23", fact.Value));
        await Turn(f, "el 23 de octubre"); await Turn(f, "16:00"); await Turn(f, "Ana Pérez"); await Turn(f, "sí"); Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task Ambiguous_locations_and_premature_yes_do_not_publish_a_reservation()
    {
        var f = Create();
        f.LocationReads.Setup(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(new[] {
            new LocationDto("sede-a", "Concepción Norte", "Synthetic", null, null, true),
            new LocationDto("sede-b", "Concepción Sur", "Synthetic", null, null, false) });
        await Turn(f, "quiero reservar limpieza dental en Concepción"); Assert.Null((await f.Context()).SelectedLocationId);
        Assert.Contains("Concepción Norte", f.Sent.Values.Last().Content); Assert.Contains("Concepción Sur", f.Sent.Values.Last().Content);
        await Turn(f, "sí"); Assert.Empty(f.Bookings); Assert.Null((await f.Context()).SelectedLocationId);
    }

    [Fact]
    public async Task An_acknowledgement_is_not_a_customer_name_or_a_premature_confirmation()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción pasado mañana"); await Turn(f, "16:00");
        await Turn(f, "Está bien"); Assert.Null((await f.Context()).RealCustomerName);
        Assert.Equal("COLLECT_CUSTOMERNAME", (await f.Context()).CurrentStep); Assert.Empty(f.Bookings);
        await Turn(f, "Ana Pérez"); await Turn(f, "sí"); Assert.Equal("Ana Pérez", Assert.Single(f.Bookings.Values).CustomerName);
    }

    [Fact]
    public async Task Instructions_during_booking_answer_then_resume_the_next_missing_business_choice()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción pasado mañana"); var before = Draft(await f.Context());
        await Turn(f, "No sé cómo reservar"); Assert.Equal(before, Draft(await f.Context()));
        var reply = f.Sent.Values.Last().Content; Assert.Contains("Para reservar", reply); Assert.Contains("horario", reply);
        Assert.DoesNotContain("empezar", reply); Assert.DoesNotContain("borrador", reply); Assert.Equal(0, f.AiCalls);
    }

    [Fact]
    public async Task Real_interpreter_json_and_pipeline_agree_on_structured_temporal_evidence()
    {
        var f = new ConciergeFixture(); var router = new Mock<IAiRouter>(MockBehavior.Strict);
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), "dentro de dos semanas", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"Intent":"Reservation","Date":"2026-11-03","Evidence":[{"Slot":"Date","Text":"dentro de dos semanas","NormalizedValue":"2026-11-03"}]}""");
        var interpreter = new AiInterpreter(router.Object, Mock.Of<IClock>(), f.Profiles.Object, NullLogger<AiInterpreter>.Instance);
        var recovery = new Mock<IConversationRecoveryService>(MockBehavior.Strict);
        recovery.Setup(r => r.AcquireAiAsync(f.Workspace, "phone", "json-date", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var pipeline = new TurnUnderstandingPipeline(f.Resolver, new DialoguePolicy(), interpreter, recovery.Object, f.Offerings.Object);
        var result = await pipeline.PrepareAsync(f.Workspace, "phone", new("instance", "phone", "Ana", "dentro de dos semanas", "json-date", false, f.Workspace),
            new() { CurrentGoal = "RESERVATION", CurrentStep = "COLLECT_DATE" }, f.Modules, default);
        var date = result.Interpretation.BookingProposal!.Understanding.Date;
        Assert.True(date.TryGetValue(out var normalized)); Assert.Equal(new DateOnly(2026, 11, 3), normalized);
        Assert.Equal(EvidenceSource.ValidatedLlm, Assert.Single(date.Candidates).Source); Assert.False(result.Interpretation.Confirmed);
    }

    [Fact]
    public async Task Semantic_start_uses_a_supported_dialogue_act_instead_of_a_reserve_keyword()
    {
        var f = Create();
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Act = DialogueAct.StartOperation,
            Evidence = [new("Act", "Me gustaría separar una limpieza dental", "StartOperation")] });
        await Turn(f, "Me gustaría separar una limpieza dental en Concepción");
        Assert.Equal("service-a", (await f.Context()).SelectedServiceId); Assert.Equal("sede-a", (await f.Context()).SelectedLocationId);
        Assert.Equal(1, f.AiCalls); Assert.Empty(f.Bookings);
        await Turn(f, "pasado mañana"); await Turn(f, "16:00"); await Turn(f, "Ana Pérez"); await Turn(f, "sí"); Assert.Single(f.Bookings);
    }

    [Fact]
    public async Task Negating_human_takeover_cannot_force_handoff_even_if_the_model_proposes_it()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción pasado mañana"); var before = Draft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Support });
        await Turn(f, "No quiero hablar con una persona"); Assert.Equal(0, f.Handoffs);
        Assert.Equal(before, Draft(await f.Context())); Assert.Equal("Automatic", (await f.Context()).Mode);
        await Turn(f, "Quiero hablar con una persona"); Assert.Equal(1, f.Handoffs); Assert.Equal("Human", (await f.Context()).Mode);
    }

    [Fact]
    public async Task A_provider_which_never_completes_is_bounded_and_local_data_can_continue()
    {
        var f = Create(); await Turn(f, "quiero limpieza dental en Concepción");
        f.DuringInterpretation = () => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        await Turn(f, "¿Qué más podríamos conversar?");
        Assert.Contains("fecha", f.Sent.Values.Last().Content); Assert.Equal(0, f.Handoffs); Assert.Empty(f.Bookings);
        f.DuringInterpretation = null; await Turn(f, "pasado mañana"); Assert.Equal("2026-10-22", (await f.Context()).TargetDate);
    }

    private async Task Turn(AssistantConversationTests.Fixture f, string text, string? source = null)
    {
        source ??= "natural-" + ++turn;
        var before = Draft(await f.Context()); var ai = f.AiCalls; var understood = f.Understood.Count;
        var mocks = new (string Name, Moq.Mock Mock)[] { ("profile", f.ProfileReads), ("locations", f.LocationReads), ("offerings", f.OfferingReads),
            ("hours", f.HoursReads), ("entitlements", f.EntitlementReads), ("knowledge", f.KnowledgeReads), ("engine", f.EngineReads) };
        var offsets = mocks.Select(m => m.Mock.Invocations.Count).ToArray();
        Exception? failure = null;
        try { await f.HandleTurn(text, source); }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            var state = await f.Context(); var interpretation = f.Understood.Count > understood ? f.Understood.Last() : null;
            var recovered = state.RecoveryLastMessageId == source && state.RecoveryLastInterpretation != null
                ? JsonSerializer.Deserialize<AiInterpretation>(state.RecoveryLastInterpretation) : null;
            var operations = mocks.SelectMany((m, i) => m.Mock.Invocations.Skip(offsets[i]).Select(call => m.Name + "." + call.Method.Name))
                .GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
            var slots = interpretation?.Interpretation.BookingProposal?.Understanding;
            var slotFailures = slots == null ? [] : new[] { DialogueFailure.FromResolution(slots.Name), DialogueFailure.FromResolution(slots.Location),
                DialogueFailure.FromResolution(slots.Service), DialogueFailure.FromResolution(slots.Date), DialogueFailure.FromResolution(slots.Time) }.OfType<DialogueFailure>().ToArray();
            output.WriteLine(JsonSerializer.Serialize(new { Source = source, Input = text, Act = interpretation?.Act.ToString() ?? "RecoveryOrSuppressed",
                Intent = interpretation?.Interpretation.Intent.ToString() ?? recovered?.Intent.ToString() ?? (state.Mode == "Human" ? "Suppressed" : "ControlOrReplay"),
                Slots = interpretation?.Interpretation.BookingProposal?.Understanding,
                Before = before, After = Draft(state), RepositoryInvocations = operations, AiCalls = f.AiCalls - ai,
                Response = f.Sent.GetValueOrDefault(source)?.Content, DeliveryStatus = f.Sent.GetValueOrDefault(source)?.Status.ToString(), Bookings = f.Bookings.Count,
                Exception = failure?.GetType().Name, SlotFailures = slotFailures.Select(s => new { Domain = s.Domain.ToString(), s.Code }),
                FailureDomain = failure is ConcurrencyException ? "Concurrency" : interpretation?.Act == DialogueAct.ProviderFailure ? "Provider"
                    : interpretation?.Act == DialogueAct.BusinessUnavailable ? "Business" : interpretation?.Act == DialogueAct.SemanticAmbiguity ? "Semantic" : null }));
        }
    }
    private static string Draft(ConversationContextDto state) => JsonSerializer.Serialize(new { state.CurrentGoal, state.CurrentStep, state.SelectedLocationId,
        state.SelectedServiceId, state.TargetDate, state.TargetTime, state.RealCustomerName, state.PendingServiceIds, state.PendingDialogueSlots, state.PreferredTime, state.Mode });
}
