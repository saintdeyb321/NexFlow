using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class SlotResolverSafetyTests
{
    [Theory]
    [InlineData("En conce", "a")]
    [InlineData("sanj eronimo", "b")]
    [InlineData("enjuague bocal", "c")]
    [InlineData("evaluación odontologixa", "d")]
    public void Conversation_fix_dynamic_entity_spelling_is_bounded_and_workspace_scoped(string text, string expected)
    {
        BusinessEntityOption[] allowed = [new("a", "Concepción"), new("b", "San Jerónimo"),
            new("c", "Enjuague bucal"), new("d", "Evaluación odontológica")];
        var result = new OfferingMentionResolver().ResolveEvidence(text, allowed);
        Assert.True(result.TryGetValue(out var entity)); Assert.Equal(expected, entity.Id);
        Assert.False(new OfferingMentionResolver().ResolveEvidence(text, []).TryGetValue(out _));
        Assert.False(new OfferingMentionResolver().ResolveEvidence(text, allowed.Concat([new BusinessEntityOption("collision", allowed.Single(o => o.Id == expected).Name)]).ToArray()).TryGetValue(out _));
    }

    [Fact]
    public void Conversation_fix_composed_relative_date_requires_absolute_clarification()
    {
        var result = new BusinessDateResolver().ResolveReservation("tras pasado mañana a las 6 de la tarde",
            new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.FromHours(-5)), true);
        Assert.False(result.TryGetValue(out _));
        Assert.NotEqual(ResolutionStatus.Determined, result.Status);
    }
    [Fact]
    public void Failure_contracts_separate_provider_semantics_business_and_uncertain_transport_without_retrying()
    {
        Assert.Equal(DialogueFailureDomain.Provider, DialogueFailure.FromProvider(NexFlow.Application.Engines.AI.AiFailureKind.InvalidResponse)!.Domain);
        Assert.Equal(DialogueFailureDomain.Semantic, DialogueFailure.FromResolution(SlotResolution<string>.Invalid("UnknownEntity"))!.Domain);
        Assert.Equal(DialogueFailureDomain.Business, DialogueFailure.FromResolution(SlotResolution<string>.Invalid("ModuleDenied"))!.Domain);
        Assert.Equal(DialogueFailureDomain.Transport, DialogueFailure.FromDelivery(MessageStatus.UnknownDelivery)!.Domain);
        Assert.Equal(DialogueFailureDomain.Provider, DialogueFailure.FromResolution(SlotResolution<string>.Unavailable())!.Domain);
        Assert.Null(DialogueFailure.FromResolution(SlotResolution<string>.Missing()));
        Assert.Null(DialogueFailure.FromDelivery(MessageStatus.Sent));
    }

    [Theory]
    [InlineData("En la sede concepción", "concepcion")]
    [InlineData("sede Concepcion", "concepcion")]
    [InlineData("en San Jerónimo", "san-jeronimo")]
    [InlineData("Sucursal Centro", "concepcion")]
    public void Authorized_aliases_and_accents_resolve_without_requiring_the_exact_label(string text, string expected)
    {
        BusinessEntityOption[] entities = [new("concepcion", "Sede Concepcion", ["Centro"]), new("san-jeronimo", "Sede San Jerónimo")];
        var result = new LocationMentionResolver().Resolve(text, entities);
        Assert.True(result.TryGetValue(out var entity)); Assert.Equal(expected, entity.Id);
    }

    [Theory]
    [InlineData("Concepción")]
    [InlineData("concepcion")]
    public void Normalization_collisions_and_shared_aliases_never_choose_an_arbitrary_location(string text)
    {
        BusinessEntityOption[] entities = [new("a", "Concepción"), new("b", "Concepcion")];
        var result = new LocationMentionResolver().Resolve(text, entities);
        Assert.Equal(ResolutionStatus.Ambiguous, result.Status); Assert.Equal(2, result.Candidates.Count);
        Assert.False(result.TryGetValue(out _));
        Assert.Equal(ResolutionStatus.Ambiguous, new LocationMentionResolver().Resolve("centro",
            [new("a", "Norte", ["Centro"]), new("b", "Sur", ["Centro"])]).Status);
    }

    [Theory]
    [InlineData("Deyvid Pariona")]
    [InlineData("Deyvid Pariona Ramos")]
    [InlineData("José del Carmen")]
    [InlineData("María-José O'Connor")]
    public void Names_are_literal_candidates_only_when_the_current_question_requests_them(string text)
    {
        var resolver = new NameSlotResolver();
        Assert.Equal(ResolutionStatus.Insufficient, resolver.Resolve(text, false).Status);
        Assert.True(resolver.Resolve(text, true).TryGetValue(out var name)); Assert.Equal(text, name);
    }

    [Theory]
    [InlineData("pasado mañana")]
    [InlineData("no sé cómo reservar")]
    [InlineData("12345678")]
    [InlineData("sí")]
    public void Non_nominal_answers_cannot_be_applied_as_customer_names(string text)
        => Assert.Equal(ResolutionStatus.Invalid, new NameSlotResolver().Resolve(text, true).Status);

    [Fact]
    public void An_early_single_given_name_remains_an_incomplete_candidate_instead_of_being_lost_or_confirmed()
    {
        var name = new NameSlotResolver().Resolve("Hola, soy Ana, quiero reservar", false);
        Assert.Equal(ResolutionStatus.Insufficient, name.Status);
        Assert.Equal("Ana", Assert.Single(name.Candidates).Value); Assert.False(name.TryGetValue(out _));
    }

    [Theory]
    [InlineData("no soy Ana Pérez")]
    [InlineData("no me llamo Ana Pérez")]
    public void Negated_presentations_cannot_be_used_as_positive_names(string text)
        => Assert.Equal(ResolutionStatus.Invalid, new NameSlotResolver().Resolve(text, true).Status);

    [Fact]
    public void The_name_span_ends_before_the_next_explicit_clause()
    {
        var result = new NameSlotResolver().Resolve("soy Ana Pérez y quiero limpieza dental", false);
        Assert.True(result.TryGetValue(out var name)); Assert.Equal("Ana Pérez", name);
    }

    [Theory]
    [InlineData("2026-12-31T16:00:00-05:00", "1 de enero", "2027-01-01")]
    [InlineData("2027-02-28T16:00:00-05:00", "mañana", "2027-03-01")]
    [InlineData("2028-02-28T16:00:00-05:00", "mañana", "2028-02-29")]
    [InlineData("2026-10-20T16:00:00-05:00", "jueves 22 de octubre", "2026-10-22")]
    public void Business_dates_handle_calendar_boundaries_and_corroborating_weekdays(string now, string text, string expected)
    {
        var resolution = new BusinessDateResolver().Resolve(text, DateTimeOffset.Parse(now));
        Assert.True(resolution.TryGetValue(out var date)); Assert.Equal(expected, date.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData("19/10/2026", ResolutionStatus.Invalid)]
    [InlineData("31/02/2027", ResolutionStatus.Invalid)]
    [InlineData("viernes 22 de octubre", ResolutionStatus.Invalid)]
    [InlineData("el viernes", ResolutionStatus.Ambiguous)]
    [InlineData("hoy o mañana", ResolutionStatus.Ambiguous)]
    [InlineData("no sé", ResolutionStatus.Insufficient)]
    public void Invalid_ambiguous_and_insufficient_dates_are_distinct(string text, ResolutionStatus expected)
        => Assert.Equal(expected, new BusinessDateResolver().Resolve(text, new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.FromHours(-5))).Status);

    [Theory]
    [InlineData("2026-10-21T02:00:00Z", "America/Lima", "2026-10-21", 20)]
    [InlineData("2026-10-21T02:00:00Z", "Europe/Madrid", "2026-10-22", 21)]
    [InlineData("2030-03-31T00:30:00Z", "Europe/Madrid", "2030-04-01", 31)]
    [InlineData("2030-03-31T01:30:00Z", "Europe/Madrid", "2030-04-01", 31)]
    public async Task Authoritative_workspace_clock_controls_relative_dates_across_midnight_and_dst(string now, string zone, string expected, int localDay)
    {
        var f = new ConciergeFixture { Now = DateTime.Parse(now, null, System.Globalization.DateTimeStyles.AdjustToUniversal), Zone = zone };
        var result = await f.Resolver.ResolveAsync(f.Workspace, "clock", "mañana", new(), SlotResolutionPurpose.Information, default);
        Assert.Equal(localDay, result.Context.LocalBusinessNow.Day);
        Assert.True(result.Date.TryGetValue(out var date)); Assert.Equal(expected, date.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData("26:00")]
    [InlineData("16:99")]
    [InlineData("16:00 por la mañana")]
    public void Invalid_time_or_conflicting_preference_is_never_an_exact_booking_time(string text)
        => Assert.Equal(ResolutionStatus.Invalid, new TimePreferenceResolver().Resolve(text).Status);

    [Fact]
    public void Afternoon_and_after_preferences_only_filter_real_engine_slots_without_inventing_a_time()
    {
        var resolver = new TimePreferenceResolver();
        var preference = resolver.Resolve("por la tarde"); Assert.True(preference.TryGetValue(out var afternoon));
        Assert.Null(afternoon.ExactTime); Assert.Equal(TimePreferenceKind.Period, afternoon.Kind);
        var date = new DateOnly(2026, 10, 22); var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        var slots = new[] { Slot(16), Slot(19), Slot(22, 30), Slot(23), Slot(21, available: false) };
        var actual = resolver.FilterAvailableSlots(slots, date, zone, afternoon);
        Assert.Equal(new[] { slots[1], slots[2] }, actual);
        Assert.True(resolver.Resolve("después de las cinco por la tarde").TryGetValue(out var after));
        Assert.Null(after.ExactTime); Assert.Equal(TimePreferenceKind.After, after.Kind);
        Assert.Equal(new[] { slots[2] }, resolver.FilterAvailableSlots(slots, date, zone, after));
        Assert.Empty(resolver.FilterAvailableSlots([], date, zone, afternoon));
        Assert.Throws<ArgumentException>(() => resolver.FilterAvailableSlots(slots, date, zone, new(TimePreferenceKind.Period)));
        static TimeSlotDto Slot(int hour, int minute = 0, bool available = true)
        { var utc = new DateTime(2026, 10, 22, hour, minute, 0, DateTimeKind.Utc); return new(utc, utc.AddMinutes(30), available); }
    }

    [Fact]
    public void Dst_repeated_local_time_preserves_both_real_utc_slots()
    {
        var resolver = new TimePreferenceResolver(); var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        TimeSlotDto[] slots = [new(new(2030, 10, 27, 0, 30, 0, DateTimeKind.Utc), new(2030, 10, 27, 1, 0, 0, DateTimeKind.Utc), true),
            new(new(2030, 10, 27, 1, 30, 0, DateTimeKind.Utc), new(2030, 10, 27, 2, 0, 0, DateTimeKind.Utc), true)];
        Assert.Equal(slots, resolver.FilterAvailableSlots(slots, new(2030, 10, 27), zone, new(TimePreferenceKind.Exact, ExactTime: new(2, 30))));
    }

    [Fact]
    public async Task Multi_slot_resolution_is_order_independent_and_never_mutates_or_confirms_the_existing_context()
    {
        var f = new ConciergeFixture(); var state = new ConversationContextDto { StateVersion = "stable", CurrentGoal = "RESERVATION", CurrentStep = "COLLECT_CUSTOMERNAME", TargetTime = "09:00" };
        var before = JsonSerializer.Serialize(state);
        var a = await f.Resolver.ResolveAsync(f.Workspace, "a", "Soy Ana Pérez, quiero limpieza dental en Concepción pasado mañana por la tarde", state, SlotResolutionPurpose.Reservation, default);
        var b = await f.Resolver.ResolveAsync(f.Workspace, "b", "Pasado mañana por la tarde, en Concepción, limpieza dental, soy Ana Pérez", state, SlotResolutionPurpose.Reservation, default);
        Assert.Equal(a.Name.Candidates[0].Value, b.Name.Candidates[0].Value);
        Assert.Equal(a.Service.Candidates[0].Value, b.Service.Candidates[0].Value);
        Assert.Equal(a.Location.Candidates[0].Value, b.Location.Candidates[0].Value);
        Assert.Equal(a.Date.Candidates[0].Value, b.Date.Candidates[0].Value);
        Assert.Equal(a.Time.Candidates[0].Value, b.Time.Candidates[0].Value);
        Assert.Equal(before, JsonSerializer.Serialize(state)); Assert.Null(a.Intent); Assert.Null(b.Intent);
    }

    [Fact]
    public async Task Licenses_are_rechecked_without_restricted_reads_and_reservation_dependencies_remain_available()
    {
        var f = new ConciergeFixture(); f.Modules.IntersectWith(["SERVICES", "RESERVATIONS"]);
        var general = await f.Resolver.ResolveAsync(f.Workspace, "info", "Concepción", new(), SlotResolutionPurpose.Information, default);
        Assert.Equal("ModuleDenied", general.Location.Code); Assert.Equal("ModuleDenied", general.Product.Code);
        f.Locations.VerifyNoOtherCalls();
        f.Offerings.Verify(o => o.GetProductsAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        var booking = await f.Resolver.ResolveAsync(f.Workspace, "booking", "Limpieza dental en Concepción", new(), SlotResolutionPurpose.Reservation, default);
        Assert.True(booking.Location.TryGetValue(out _)); Assert.True(booking.Service.TryGetValue(out _));
        f.Modules.Remove("SERVICES"); f.Locations.Invocations.Clear(); f.Offerings.Invocations.Clear();
        var revoked = await f.Resolver.ResolveAsync(f.Workspace, "revoked", "Concepción", new(), SlotResolutionPurpose.Reservation, default);
        Assert.Equal("ModuleDenied", revoked.Location.Code); Assert.Equal("ModuleDenied", revoked.Service.Code);
        f.Locations.VerifyNoOtherCalls(); f.Offerings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Active_offerings_are_filtered_by_location_and_reservability_before_resolution()
    {
        var f = new ConciergeFixture(); var state = new ConversationContextDto { SelectedLocationId = "concepcion", CurrentStep = "COLLECT_SERVICE" };
        foreach (var text in new[] { "Servicio retirado", "Servicio privado", "Consulta informativa" })
            Assert.Equal(ResolutionStatus.Invalid, (await f.Resolver.ResolveAsync(f.Workspace, text, text, state, SlotResolutionPurpose.Reservation, default)).Service.Status);
        state.SelectedLocationId = "san-jeronimo";
        Assert.True((await f.Resolver.ResolveAsync(f.Workspace, "other", "Servicio privado", state, SlotResolutionPurpose.Reservation, default)).Service.TryGetValue(out var service));
        Assert.Equal("other-location", service.Id);
        Assert.Equal("san-jeronimo", service.LocationId);
    }

    [Fact]
    public async Task Equal_names_in_separate_workspaces_never_share_authorization_or_catalog_state()
    {
        var a = new ConciergeFixture(); var b = new ConciergeFixture();
        var ar = await a.Resolver.ResolveAsync(a.Workspace, "same", "Limpieza dental", new(), SlotResolutionPurpose.Information, default);
        var br = await b.Resolver.ResolveAsync(b.Workspace, "same", "Limpieza dental", new(), SlotResolutionPurpose.Information, default);
        Assert.True(ar.Service.TryGetValue(out _)); Assert.True(br.Service.TryGetValue(out _));
        Assert.NotEqual(ar.Context.WorkspaceId, br.Context.WorkspaceId);
        b.Modules.Remove("SERVICES");
        Assert.Equal("ModuleDenied", (await b.Resolver.ResolveAsync(b.Workspace, "revoked", "Limpieza dental", new(), SlotResolutionPurpose.Information, default)).Service.Code);
        Assert.Contains("SERVICES", ar.Context.EnabledModules); Assert.Contains("SERVICES", br.Context.EnabledModules);
        await Assert.ThrowsAsync<Moq.MockException>(() => a.Resolver.ResolveAsync(b.Workspace, "wrong", "Limpieza dental", new(), SlotResolutionPurpose.Information, default));
    }

    [Fact]
    public async Task Human_control_cancellation_and_invalid_zone_fail_closed_without_changing_state()
    {
        var f = new ConciergeFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Resolver.ResolveAsync(f.Workspace, "human", "hola", new() { Mode = "Human" }, SlotResolutionPurpose.Information, default));
        f.Entitlements.VerifyNoOtherCalls(); f.Profiles.VerifyNoOtherCalls();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Resolver.ResolveAsync(f.Workspace, "cancelled", "mañana", new(), SlotResolutionPurpose.Information, cancelled.Token));
        f.Entitlements.VerifyNoOtherCalls();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Resolver.ResolveAsync(f.Workspace, "oversized", new string('x', 2001), new(), SlotResolutionPurpose.Information, default));
        f.Entitlements.VerifyNoOtherCalls();
        f.Zone = "invalid/zone";
        await Assert.ThrowsAsync<TimeZoneNotFoundException>(() => f.Resolver.ResolveAsync(f.Workspace, "zone", "mañana", new(), SlotResolutionPurpose.Information, default));
        f.Locations.VerifyNoOtherCalls(); f.Offerings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Telemetry_contains_only_workspace_and_statuses_even_when_the_message_contains_personal_data()
    {
        var logs = new List<string>(); var logger = new Mock<ILogger<WorkspaceSlotResolver>>();
        logger.Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(call => logs.Add(call.Arguments[2].ToString()!)));
        var f = new ConciergeFixture(logger: logger.Object);
        await f.Resolver.ResolveAsync(f.Workspace, "private-provider-id", "Soy Ana Pérez, teléfono 51999999999, quiero limpieza dental", new(), SlotResolutionPurpose.Reservation, default);
        var trace = Assert.Single(logs); Assert.Contains(f.Workspace.ToString(), trace);
        Assert.DoesNotContain("Ana", trace); Assert.DoesNotContain("51999999999", trace); Assert.DoesNotContain("private-provider-id", trace);
        Assert.DoesNotContain("limpieza", trace, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Incident_legacy_location_failure_is_reproduced_but_phase_two_local_slots_no_longer_depend_on_the_provider()
    {
        var f = new ConciergeFixture();
        var legacy = new LocationResolverService(f.Locations.Object);
        Assert.Null(await legacy.ResolveLocationIdAsync(f.Workspace, "En la sede concepción", default));
        var resolved = await f.Resolver.ResolveAsync(f.Workspace, "incident", "En la sede concepción", new(), SlotResolutionPurpose.Reservation, default);
        Assert.True(resolved.Location.TryGetValue(out var location)); Assert.Equal("concepcion", location.Id);
        var conversation = new AssistantConversationTests.Fixture(conciergeCatalog: true);
        await conversation.Turn("Hola");
        var draft = await conversation.Context(); draft.CurrentGoal = "RESERVATION"; draft.CurrentStep = "COLLECT_CUSTOMERNAME";
        await conversation.Contexts.SaveContextAsync(conversation.Workspace, AssistantConversationTests.Fixture.Phone, draft, default);
        foreach (var text in new[] { "Deyvid Pariona", "En la sede concepción", "pasado mañana" })
        {
            conversation.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProviderUnavailable, Failure = NexFlow.Application.Engines.AI.AiFailureKind.Unavailable });
            await conversation.Turn(text);
        }
        Assert.Equal(0, conversation.Handoffs); Assert.Equal(0, conversation.AiCalls); Assert.Empty(conversation.Bookings);
        var final = await conversation.Context(); Assert.Equal("Deyvid Pariona", final.RealCustomerName);
        Assert.Equal("sede-a", final.SelectedLocationId); Assert.NotNull(final.TargetDate);
    }
}
