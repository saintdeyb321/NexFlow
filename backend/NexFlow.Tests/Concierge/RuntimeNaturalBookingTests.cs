using Moq;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Reservations;
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
}
