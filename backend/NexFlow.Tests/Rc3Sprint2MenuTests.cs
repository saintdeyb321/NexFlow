using Microsoft.Extensions.Options;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3Sprint2MenuTests
{
    [Theory]
    [InlineData("1")] [InlineData("opción 1")] [InlineData("Escojo la opción 1")]
    [InlineData("elijo 1")] [InlineData("selecciono la primera")] [InlineData("número uno")]
    public async Task Explicit_selection_uses_the_current_authorized_menu_without_ai_or_business_effects(string text)
    {
        var f = new AssistantConversationTests.Fixture(); await f.Turn("menú");
        await f.Turn(text, "selection"); await f.Turn(text, "selection");
        Assert.Equal(0, f.AiCalls); Assert.Equal(2, f.Sent.Count); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("la segunda", ConversationIntent.ServiceQuery)]
    [InlineData("quiero la opción 3", ConversationIntent.Reservation)]
    public async Task Selection_index_identifies_the_real_option(string text, ConversationIntent expected)
    {
        var f = new AssistantConversationTests.Fixture(); await f.Turn("menú"); var context = await f.Context();
        var result = await Recover(f, context, text);
        Assert.Equal(expected, result!.Interpretation!.Intent); Assert.False(result.Interpretation.Confirmed);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("mi DNI es 12345678")] [InlineData("12345678")] [InlineData("51999999999")]
    [InlineData("quiero 3 productos")] [InlineData("3 unidades")] [InlineData("S/ 1.00")]
    [InlineData("2026-10-20")] [InlineData("reservar a las 10")] [InlineData("10:00")]
    [InlineData("elijo 1 y reserva mañana")] [InlineData("opción 1 o 2")]
    public async Task Numeric_business_information_is_never_extracted_as_a_menu_choice(string text)
    {
        var f = new AssistantConversationTests.Fixture(); await f.Turn("menú"); var c = await f.Context();
        Assert.Null(await Recover(f, c, text)); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("0")] [InlineData("6")] [InlineData("opción 99")]
    public async Task Out_of_range_choices_refresh_the_menu_instead_of_executing_an_action(string text)
    {
        var f = new AssistantConversationTests.Fixture(); await f.Turn("menú");
        var result = await Recover(f, await f.Context(), text);
        Assert.NotNull(result!.Response); Assert.Null(result.Interpretation); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData("location")] [InlineData("service")] [InlineData("expired")]
    public async Task Changed_context_or_expired_menu_cannot_execute_the_previous_selection(string change)
    {
        var f = new AssistantConversationTests.Fixture(); await f.Turn("menú"); var c = await f.Context();
        if (change == "location") c.SelectedLocationId = "another-location";
        if (change == "service") c.SelectedServiceId = "another-service";
        if (change == "expired") c.LastUpdated = f.Now.AddHours(-25);
        var result = await Recover(f, c, "Escojo la opción 1");
        Assert.NotNull(result!.Response); Assert.Null(result.Interpretation); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Revoked_module_and_removed_offering_cannot_be_selected_from_an_old_menu()
    {
        var f = new AssistantConversationTests.Fixture(); await f.Turn("menú"); f.Modules.Remove("CATALOG");
        var result = await Recover(f, await f.Context(), "opción 1");
        Assert.Contains("ya no está disponible", result!.Response); Assert.Null(result.Interpretation);

        // An offering still licensed but removed from authoritative business data.
        var offerings = new Mock<IOfferingService>();
        offerings.Setup(o => o.GetProductsAsync(f.Workspace, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ProductDto>());
        var recovery = new ConversationRecoveryService(f.Contexts, Mock.Of<IConversationStateRepository>(), offerings.Object,
            Mock.Of<ILocationRepository>(), Mock.Of<IBusinessHoursRepository>(), Mock.Of<IReservationEngine>(),
            Mock.Of<IClock>(clock => clock.UtcNow == f.Now), Options.Create(new AssistantRecoveryOptions()), Mock.Of<IBusinessProfileRepository>());
        f.Modules.Add("CATALOG"); await f.Turn("menú", "refresh-products");
        var context = await f.Context();
        result = await recovery.RecoverAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, "chat", Command(f, "opción 1"), context, f.Modules, default);
        Assert.Contains("ya no está disponible", result!.Response); Assert.Null(result.Interpretation);
    }

    [Fact]
    public async Task Current_page_numbering_and_replay_preserve_the_draft_without_committing_a_reservation()
    {
        var f = new AssistantConversationTests.Fixture(8); await f.SeedDraft("RESERVATION"); var c = await f.Context(); c.CurrentStep = "COLLECT_SERVICE";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, c, default);
        await f.Turn("menú"); await f.Turn("ver más"); var page = await f.Context();
        var selected = page.RecoveryOptions[1]; Assert.Equal(5, page.RecoveryOptionsOffset);
        var result = await Recover(f, page, "la segunda");
        Assert.Equal(selected.Key, result!.Interpretation!.Service); Assert.False(result.Interpretation.Confirmed);
        var replay = await Recover(f, await f.Context(), "la segunda");
        Assert.Equal(selected.Key, replay!.Interpretation!.Service); Assert.Empty(f.Bookings); Assert.Empty(f.Orders); Assert.Equal(0, f.AiCalls);
    }

    private static ProcessIncomingMessageCommand Command(AssistantConversationTests.Fixture f, string text) => new("instance", AssistantConversationTests.Fixture.Phone, "Cliente", text, "strict-selection", false, f.Workspace);
    private static Task<RecoveryDecision?> Recover(AssistantConversationTests.Fixture f, ConversationContextDto context, string text)
        => f.Recovery.RecoverAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, "chat", Command(f, text), context, f.Modules, default);
}
