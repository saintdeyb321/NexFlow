using Moq;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Enums;
using NexFlow.Domain.ValueObjects;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Features.Business;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class ReleaseClosureTests
{
    private static AssistantConversationTests.Fixture Create()
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true) {
            Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc)
        };
        f.UseCatalogKnowledge(); return f;
    }

    [Fact]
    public async Task Generic_pdf_with_both_modules_asks_for_scope_before_reading_an_artifact()
    {
        var f = Create(); await f.HandleTurn("¿Me puedes pasar el PDF?", "pdf");
        Assert.Contains("productos", f.Sent["pdf"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("servicios", f.Sent["pdf"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "ProductQuery", "ServiceQuery" }, (await f.Context()).RecoveryOptions.Select(o => o.Key));
        Assert.Empty(f.CatalogReads.Invocations);
        Assert.DoesNotContain(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData("2026-10-20T16:00:00-05:00", "el jueves", "2026-10-22")]
    [InlineData("2026-10-31T16:00:00-05:00", "el lunes", "2026-11-02")]
    [InlineData("2026-12-31T16:00:00-05:00", "el viernes", "2027-01-01")]
    [InlineData("2026-10-25T16:00:00-05:00", "el lunes", "2026-10-26")]
    [InlineData("2026-10-20T16:00:00-05:00", "el martes a las 15:00", "2026-10-27")]
    [InlineData("2026-10-20T16:00:00-05:00", "el jueves de la próxima semana", "2026-10-29")]
    [InlineData("2026-10-20T16:00:00-05:00", "el lunes de la semana que viene", "2026-10-26")]
    [InlineData("2026-10-20T16:00:00-05:00", "el jueves de esta semana", "2026-10-22")]
    public void Reservation_weekdays_use_business_calendar_and_explicit_weeks(string clock, string text, string expected)
    {
        var result = new BusinessDateResolver().ResolveReservation(text, DateTimeOffset.Parse(clock));
        Assert.True(result.TryGetValue(out var date), result.Code);
        Assert.Equal(DateOnly.Parse(expected), date);
    }

    [Fact]
    public void Contextual_week_and_ambiguity_are_preserved_without_changing_other_domains()
    {
        var resolver = new BusinessDateResolver(); var now = new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(new DateOnly(2026, 10, 29), Assert.Single(resolver.ResolveReservation("el jueves", now,
            previousQuestion: "¿Qué día de la próxima semana deseas?").Candidates).Value);
        Assert.Equal(new DateOnly(2026, 10, 30), Assert.Single(resolver.ResolveReservation("mejor el viernes", now,
            contextDate: new(2026, 10, 29)).Candidates).Value);
        Assert.Equal(ResolutionStatus.Ambiguous, resolver.ResolveReservation("el jueves de otra semana", now).Status);
        Assert.Equal(ResolutionStatus.Invalid, resolver.ResolveReservation("el lunes de esta semana", now).Status);
        Assert.Equal(ResolutionStatus.Ambiguous, resolver.ResolveReservation("el jueves o el viernes", now).Status);
        Assert.Equal(ResolutionStatus.Ambiguous, resolver.Resolve("el jueves", now).Status);
        foreach (var day in new[] { "lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo" })
        {
            Assert.True(resolver.ResolveReservation(day, now).TryGetValue(out var date));
            Assert.InRange(date.DayNumber - DateOnly.FromDateTime(now.Date).DayNumber, 1, 7);
        }
    }

    [Theory]
    [InlineData("America/Lima", "2026-10-21")]
    [InlineData("Asia/Tokyo", "2026-10-28")]
    public async Task Workspace_profile_clock_controls_the_weekday_across_utc_midnight(string zone, string expected)
    {
        var f = Create(); f.Now = new(2026, 10, 21, 2, 0, 0, DateTimeKind.Utc);
        f.ProfileReads.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessProfileDto("Synthetic", "", "", "", "", zone));
        await f.HandleTurn("Quiero reservar limpieza dental el miércoles por la tarde", "booking");
        Assert.Equal(expected, (await f.Context()).TargetDate); Assert.Empty(f.Bookings);
        Assert.All(f.ProfileReads.Invocations, i => Assert.Equal(f.Workspace, i.Arguments[0]));
    }

    [Fact]
    public async Task Future_weekday_retains_time_preference_announces_full_date_and_requires_confirmation()
    {
        var f = Create(); await f.HandleTurn("Quiero una limpieza dental el jueves por la tarde", "booking");
        var state = await f.Context(); Assert.Equal("2026-10-22", state.TargetDate); Assert.Null(state.TargetTime);
        Assert.NotNull(state.PreferredTime); Assert.Contains("22 de octubre de 2026", f.Sent["booking"].Content);
        Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
        await f.HandleTurn("en Concepción", "location");
        Assert.DoesNotContain("16:00", f.Sent["location"].Content); Assert.Null((await f.Context()).TargetTime);
        await f.HandleTurn("muéstrame opciones", "explicit-options");
        Assert.Contains("16:00", f.Sent["explicit-options"].Content);
        await f.HandleTurn("16:00", "time"); Assert.Empty(f.Bookings);
        await f.HandleTurn("mejor el viernes", "correction");
        Assert.Equal("2026-10-23", (await f.Context()).TargetDate);
        Assert.Null((await f.Context()).TargetTime); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("el jueves por la tarde", "2026-10-22")]
    [InlineData("hoy a las 15:00", "2026-10-20")]
    [InlineData("el 1 de enero de 2027 por la tarde", "2027-01-01")]
    public async Task Missing_availability_or_engine_horizon_does_not_roll_date_or_invent_time(string text, string expected)
    {
        var f = Create();
        // The engine is authoritative for availability, including any deployment horizon.
        f.EngineReads.Setup(e => e.GetAvailabilityAsync(f.Workspace, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TimeSlotDto>());
        await f.HandleTurn("Quiero reservar limpieza dental en Concepción " + text, "empty");
        Assert.Equal(expected, (await f.Context()).TargetDate); Assert.Null((await f.Context()).TargetTime);
        Assert.Empty(f.Bookings);
        Assert.DoesNotContain(f.EngineReads.Invocations, i => i.Method.Name == "CreateReservationAsync");
    }

    [Theory]
    [InlineData("PRODUCT")]
    [InlineData("SERVICE")]
    public async Task Single_licensed_catalog_uses_only_its_current_pdf(string scope)
    {
        var f = Create(); f.Modules.IntersectWith([scope == "PRODUCT" ? "CATALOG" : "SERVICES"]);
        Current(f, scope);
        await f.HandleTurn("¿Me puedes pasar el PDF?", "pdf");
        AssertDocument(f, scope); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData("PRODUCT", "1", MessageStatus.Sent)]
    [InlineData("SERVICE", "Servicios", MessageStatus.Sent)]
    [InlineData("PRODUCT", "Productos", MessageStatus.UnknownDelivery)]
    [InlineData("SERVICE", "2", MessageStatus.UnknownDelivery)]
    public async Task Persisted_catalog_choice_uses_durable_delivery_and_does_not_repeat_uncertain_messages(string scope, string choice, MessageStatus delivery)
    {
        var f = Create(); Current(f, scope); f.DeliveryStatus = delivery;
        await f.HandleTurn("¿Me puedes pasar el PDF?", "question");
        Assert.Empty(f.CatalogReads.Invocations);
        await f.HandleTurn(choice, "selection"); await f.HandleTurn(choice, "selection");
        AssertDocument(f, scope); Assert.Equal(delivery, f.Sent["selection"].Status);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData("PRODUCT", false)]
    [InlineData("SERVICE", false)]
    [InlineData("PRODUCT", true)]
    [InlineData("SERVICE", true)]
    public async Task Missing_or_generating_selected_pdf_uses_facts_without_delivering_previous_file(string scope, bool generating)
    {
        var f = Create();
        if (generating) {
            var artifact = Current(f, scope);
            artifact.MarkAsGenerating("new-source", "generation", ArtifactDesign.Create("MODERN", "OCEAN", "BALANCED"), f.Now);
        }
        await f.HandleTurn("¿Me puedes pasar el PDF?", "question");
        await f.HandleTurn(scope == "PRODUCT" ? "1" : "2", "selection");
        Assert.Contains(scope == "PRODUCT" ? "Pan integral" : "Limpieza dental", f.Sent["selection"].Content);
        Assert.DoesNotContain(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.All(f.CatalogReads.Invocations, i => { Assert.Equal("GetArtifactAsync", i.Method.Name); Assert.Equal(scope, i.Arguments[1]); });
    }

    [Theory]
    [InlineData("PRODUCT")]
    [InlineData("SERVICE")]
    public async Task Valid_reference_disambiguates_but_inactive_or_foreign_reference_does_not(string scope)
    {
        foreach (var validity in new[] { "valid", "inactive", "foreign" })
        {
            var f = Create(); Current(f, scope); var state = await f.Context();
            new OfferingReference(validity == "foreign" ? Guid.NewGuid() : f.Workspace, scope,
                [scope == "PRODUCT" ? "product-a" : "service-a"], null).Remember(state);
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
            if (validity == "inactive") { if (scope == "PRODUCT") f.Products[0].IsActive = false; else f.Services[0].IsActive = false; }
            await f.HandleTurn("¿Me puedes pasar el PDF?", "pdf");
            if (validity == "valid") AssertDocument(f, scope);
            else { Assert.Empty(f.CatalogReads.Invocations); Assert.Equal(2, (await f.Context()).RecoveryOptions.Count); }
            Assert.All(f.OfferingReads.Invocations, i => Assert.Equal(f.Workspace, i.Arguments[0]));
        }
    }

    [Theory]
    [InlineData("PRODUCT", false)]
    [InlineData("SERVICE", false)]
    [InlineData("PRODUCT", true)]
    [InlineData("SERVICE", true)]
    public async Task Revocation_before_choice_or_during_lookup_blocks_restricted_delivery(string scope, bool duringLookup)
    {
        var f = Create(); var artifact = Current(f, scope); var module = scope == "PRODUCT" ? "CATALOG" : "SERVICES";
        await f.HandleTurn("¿Me puedes pasar el PDF?", "question");
        if (duringLookup) f.CatalogReads.Setup(c => c.GetArtifactAsync(f.Workspace, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { f.Modules.Remove(module); return artifact; });
        else f.Modules.Remove(module);
        await f.HandleTurn(scope == "PRODUCT" ? "1" : "2", "selection");
        Assert.DoesNotContain(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.DoesNotContain(scope == "PRODUCT" ? "Pan integral" : "Limpieza dental", f.Sent["selection"].Content);
    }

    [Fact]
    public async Task Catalog_scope_question_during_incomplete_booking_preserves_the_draft()
    {
        var f = Create(); var state = await f.Context(); state.CurrentGoal = "BOOKING"; state.CurrentStep = "COLLECT_SERVICE"; state.SelectedLocationId = "sede-a";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("¿Me puedes pasar el PDF?", "pdf");
        Assert.Equal(2, (await f.Context()).RecoveryOptions.Count);
        await f.HandleTurn("Productos", "selection");
        Assert.Contains("Pan integral", f.Sent["selection"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
    }

    private static CatalogArtifact Current(AssistantConversationTests.Fixture f, string scope)
    {
        var artifact = CatalogArtifact.Initialize(f.Workspace, scope);
        artifact.CompleteUpload("https://example.test/current.pdf", "synthetic-hash");
        f.CatalogReads.Setup(c => c.GetArtifactAsync(f.Workspace, scope, It.IsAny<CancellationToken>())).ReturnsAsync(artifact);
        return artifact;
    }

    private static void AssertDocument(AssistantConversationTests.Fixture f, string scope)
    {
        var call = Assert.Single(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.Equal(f.Workspace, call.Arguments[0]); Assert.Equal(scope, call.Arguments[6]);
        Assert.All(f.CatalogReads.Invocations, i => { Assert.Equal("GetArtifactAsync", i.Method.Name); Assert.Equal(f.Workspace, i.Arguments[0]); Assert.Equal(scope, i.Arguments[1]); });
    }
}

