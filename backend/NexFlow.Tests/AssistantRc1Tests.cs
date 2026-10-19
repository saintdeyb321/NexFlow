using System.Text.Json;
using Moq;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Knowledge;
using Xunit;

namespace NexFlow.Tests;

public sealed class AssistantRc1Tests
{
    [Fact]
    public async Task Repeated_labels_require_a_number_instead_of_selecting_the_first_match()
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("RESERVATION");
        var c = await f.Context(); c.CurrentStep = "COLLECT_SERVICE"; c.SelectedServiceId = null;
        c.RecoveryOptions = new() { new("service-a", "Servicio común", "Service"), new("service-b", "Servicio común", "Service") };
        c.RecoveryOptionsContext = $"{c.CurrentGoal}:{c.CurrentStep}:{c.SelectedLocationId}:{c.SelectedServiceId}:{c.TargetDate}";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, c, default);
        await f.Turn("Servicio común"); Assert.Null((await f.Context()).SelectedServiceId); Assert.Equal(0, f.AiCalls); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Booking_picker_shows_each_offering_once_and_only_one_numbered_list()
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("RESERVATION");
        var c = await f.Context(); c.CurrentStep = "COLLECT_LOCATION"; c.SelectedLocationId = null; c.SelectedServiceId = null;
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, c, default);
        await f.Turn("menú"); await f.Turn("1");
        var text = f.Sent.Values.Last().Content;
        Assert.Equal(1, text.Split("Servicio A").Length - 1); Assert.Equal(1, text.Split("Servicio B").Length - 1);
        Assert.DoesNotContain("• ", text); Assert.Contains("1. Servicio A", text); Assert.Equal("COLLECT_SERVICE", (await f.Context()).CurrentStep);
    }

    [Fact]
    public async Task A_human_request_containing_hours_does_not_get_swallowed_by_the_knowledge_shortcut()
    {
        var w = Guid.NewGuid(); var command = new ProcessIncomingMessageCommand("instance", "+51999999999", "Ana", "quiero hablar con un asesor sobre el horario", "source", false, w);
        var guard = new Mock<IIncomingMessageGuard>(); guard.Setup(g => g.CheckMessageAsync(command, default)).ReturnsAsync((true, w, command.CustomerPhone));
        var states = new Mock<IConversationStateService>(); var chat = new ConversationRecord { Id = "chat" };
        states.Setup(s => s.ProcessStateAsync(w, command.CustomerPhone, command, default)).ReturnsAsync((true, chat, (string?)null));
        var ai = new Mock<IAiResponseOrchestrator>(); ai.Setup(a => a.RespondAsync(w, command.CustomerPhone, command, chat, default)).ReturnsAsync(new MessageRecord { Status = MessageStatus.Sent });
        var outbound = new Mock<IOutboundMessageService>(); outbound.Setup(o => o.TryResumeResponseAsync(w, "source", default)).ReturnsAsync(false);
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var handler = new ProcessIncomingMessageCommandHandler(guard.Object, states.Object, ai.Object, knowledge.Object, outbound.Object,
            NullLogger<ProcessIncomingMessageCommandHandler>.Instance, Mock.Of<IContextRecoveryService>(MockBehavior.Strict), Mock.Of<ILocationRepository>(MockBehavior.Strict));
        await handler.Handle(command, default); ai.Verify(a => a.RespondAsync(w, command.CustomerPhone, command, chat, default), Times.Once()); knowledge.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Opening_menu_resets_pagination_and_last_page_does_not_wrap()
    {
        var f = new AssistantConversationTests.Fixture();
        await f.Turn("hola"); await f.Turn("ver más");
        var last = JsonSerializer.Serialize((await f.Context()).RecoveryOptions);
        await f.Turn("ver más"); Assert.Equal(last, JsonSerializer.Serialize((await f.Context()).RecoveryOptions));
        await f.Turn("menú"); Assert.Equal("ProductQuery", (await f.Context()).RecoveryOptions[0].Key);
    }

    [Fact]
    public async Task Changing_menu_context_cannot_inherit_another_menus_offset()
    {
        var f = new AssistantConversationTests.Fixture(8); await f.Turn("hola"); await f.Turn("ver más");
        var c = await f.Context(); c.CurrentGoal = "RESERVATION"; c.CurrentStep = "COLLECT_SERVICE"; c.SelectedLocationId = "sede-a";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, c, default);
        await f.Turn("menú"); Assert.Equal("service-2", (await f.Context()).RecoveryOptions[0].Key);
        Assert.Equal(0, (await f.Context()).RecoveryOptionsOffset);
    }

    [Fact]
    public async Task Pending_cancellation_blocks_a_model_change_directive_even_in_a_question()
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("RESERVATION");
        await f.Turn("cancelar"); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.ChangeService });
        await f.Turn("¿qué pasa si cambio el servicio?");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal("CANCEL", (await f.Context()).PendingRecoveryAction); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_different_transaction_intent_must_not_be_applied_to_the_existing_draft()
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Date = "2026-10-20", SearchTerm = "2x Producto real" });
        await f.Turn("quiero pedir para el 20 de octubre");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task An_older_budget_request_cannot_reset_a_newer_windows_counter()
    {
        var repo = new FirestoreConversationStateRepository(new InMemoryFirestore().Db); var w = Guid.NewGuid(); var now = DateTime.UtcNow;
        Assert.True(await repo.TryConsumeAiBudgetAsync(w, "consumer", "newer", now.AddMinutes(1), 1, 1, default));
        Assert.False(await repo.TryConsumeAiBudgetAsync(w, "other", "late", now, 1, 1, default));
        Assert.False(await repo.TryConsumeAiBudgetAsync(w, "third", "overflow", now.AddMinutes(1), 1, 1, default));
    }
}
