using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class OrderConfirmationAuditTests
{
    private static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true, intelligentCommerce: true)
        { Now = new DateTime(2026, 10, 26, 15, 0, 0, DateTimeKind.Utc) };

    private static AiInterpretation Finish(string text) => new() {
        Intent = ConversationIntent.Order, Act = DialogueAct.Resume, OrderAction = OrderTurnKind.Submit,
        Evidence = [new("Act", text, nameof(DialogueAct.Resume))]
    };

    private static async Task<AssistantConversationTests.Fixture> Reviewed()
    {
        var f = Create();
        var state = await f.Context(); state.SelectedLocationId = "sede-a";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("Quiero hacer un pedido", "start");
        await f.HandleTurn("dos panes integrales y un queque", "items");
        const string finish = "He terminado de elegir los artículos";
        f.Interpretations.Enqueue(Finish(finish));
        await f.HandleTurn(finish, "review");
        Assert.Empty(f.Orders);
        Assert.StartsWith("ORDER_CONFIRM:", (await f.Context()).CurrentStep);
        Assert.Equal(ExpectedReply.ConfirmOrder, (await f.Context()).CurrentQuestion?.Expected);
        Assert.Contains("2 × Pan integral", f.Sent["review"].Content);
        Assert.Contains("1 × Queque", f.Sent["review"].Content);
        Assert.Contains("S/ 15.00", f.Sent["review"].Content);
        return f;
    }

    [Fact]
    public async Task Finishing_selection_prepares_review_without_consent_or_order()
    {
        var f = await Reviewed();
        var proposal = Assert.Single(f.Understood, t => t.Interpretation.OrderProposal?.Kind == OrderTurnKind.Submit).Interpretation.OrderProposal!;
        Assert.True(proposal.RequireConfirmation);
        Assert.DoesNotContain(f.OrderWrites.Invocations, i => i.Method.Name == "CreateOrderAsync");
        Assert.Equal(new[] { 2, 1 }, (await f.Context()).OrderDraftItems.Select(i => i.Quantity));
    }

    [Theory]
    [InlineData("Adelante, remite al negocio lo que acabas de resumir")]
    [InlineData("Doy mi visto bueno para que tramiten esa solicitud tal cual")]
    [InlineData("Ya lo revisé; pueden remitir la solicitud con esas cantidades e importes")]
    public async Task Free_authorization_receives_a_short_reminder_and_explicit_confirmation_completes(string text)
    {
        var f = await Reviewed(); var before = await f.Context();
        var step = before.CurrentStep; var question = before.CurrentQuestion!.QuestionId;
        var items = before.OrderDraftItems.Select(i => (i.ProductId, i.Quantity)).ToArray();
        foreach (var source in new[] { "natural-authorization", "natural-authorization-again" })
        {
            f.Interpretations.Enqueue(Finish(text));
            await f.HandleTurn(text, source);
            Assert.True(f.Understood.Last().UsedAi);
            Assert.True(f.Understood.Last().Interpretation.OrderProposal!.RequireConfirmation);
            Assert.Equal(step, (await f.Context()).CurrentStep);
            Assert.Equal(items, (await f.Context()).OrderDraftItems.Select(i => (i.ProductId, i.Quantity)).ToArray());
            Assert.Equal(OperationStatus.AwaitingConfirmation, f.Plans.Last().Operation);
            Assert.DoesNotContain("2 × Pan integral", f.Sent[source].Content);
            Assert.Contains("*Confirmar*", f.Sent[source].Content);
            Assert.Empty(f.Orders);
        }
        Assert.Equal(question, (await f.Context()).CurrentQuestion!.QuestionId);
        // Positive control: the same unchanged draft really can be committed.
        await f.HandleTurn("Confirmar", "explicit-consent");
        Assert.Equal(OrderStatus.PendingReview, Assert.Single(f.Orders.Values).Status);
    }

    [Theory]
    [InlineData("Confirmar")]
    [InlineData("Sí")]
    public async Task Existing_bounded_consent_can_submit_the_current_quote(string text)
    {
        var f = await Reviewed(); await f.HandleTurn(text, "consent");
        var order = Assert.Single(f.Orders.Values);
        Assert.Equal(OrderStatus.PendingReview, order.Status); Assert.Equal(1500, order.TotalAmountMinorUnits);
        Assert.Equal("sede-a", order.RequestedLocationId);
        Assert.Single(f.OrderWrites.Invocations, i => i.Method.Name == "CreateOrderAsync");
    }

    [Fact]
    public async Task Finalizing_from_review_requires_a_separate_confirmation_message()
    {
        var f = Create(); await f.HandleTurn("Quiero hacer un pedido", "start");
        await f.HandleTurn("dos panes integrales y un queque", "items");
        Assert.StartsWith("ORDER_REVIEW:", (await f.Context()).CurrentStep);
        await f.HandleTurn("finalizar", "finish-selection");
        Assert.Empty(f.Orders); Assert.StartsWith("ORDER_CONFIRM:", (await f.Context()).CurrentStep);
        await f.HandleTurn("Confirmar", "separate-consent"); Assert.Single(f.Orders);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("workspace")]
    [InlineData("intent")]
    [InlineData("conversation")]
    [InlineData("fingerprint")]
    [InlineData("future")]
    [InlineData("expected-reply")]
    public async Task Invalid_confirmation_reference_issues_a_new_review_without_submitting(string reason)
    {
        var f = await Reviewed(); var state = await f.Context(); var oldQuestion = state.CurrentQuestion!.QuestionId;
        if (reason == "missing") state.CurrentQuestion = null;
        if (reason == "expired") f.Now = f.Now.AddMinutes(10).AddSeconds(1);
        if (reason == "workspace") state.CurrentQuestion = state.CurrentQuestion! with { WorkspaceId = Guid.NewGuid() };
        if (reason == "intent") state.CurrentQuestion = state.CurrentQuestion! with { Intent = ConversationIntent.Reservation };
        if (reason == "conversation") state.CurrentQuestion = state.CurrentQuestion! with { ConversationId = "another-conversation" };
        if (reason == "fingerprint") state.CurrentQuestion = state.CurrentQuestion! with { DraftFingerprint = "obsolete" };
        if (reason == "future") state.CurrentQuestion = state.CurrentQuestion! with { IssuedAt = f.Now.AddMinutes(1) };
        if (reason == "expected-reply") state.CurrentQuestion = state.CurrentQuestion! with { Expected = ExpectedReply.RequestedData };
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("Confirmar", "invalid-question");
        Assert.Empty(f.Orders); var updated = await f.Context();
        Assert.Equal(ExpectedReply.ConfirmOrder, updated.CurrentQuestion?.Expected);
        Assert.Equal(ConversationIntent.Order, updated.CurrentQuestion?.Intent);
        Assert.Equal(f.Workspace, updated.CurrentQuestion?.WorkspaceId);
        Assert.NotEqual(oldQuestion, updated.CurrentQuestion?.QuestionId);
        Assert.Contains("2 × Pan integral", f.Sent["invalid-question"].Content);
        await f.HandleTurn("Confirmar", "renewed-consent"); Assert.Single(f.Orders);
    }

    [Fact]
    public async Task The_review_displays_the_authorized_location_and_deleted_locations_cannot_confirm()
    {
        var f = await Reviewed(); Assert.Contains("Sede Concepcion", f.Sent["review"].Content);
        f.LocationReads.Setup(r => r.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NexFlow.Application.Features.Business.LocationDto>());
        await f.HandleTurn("Confirmar", "deleted-location"); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Short_reminders_do_not_extend_the_original_ten_minute_consent_window()
    {
        var f = await Reviewed(); var expires = (await f.Context()).CurrentQuestion!.ExpiresAt;
        f.Now = f.Now.AddMinutes(9);
        const string text = "Doy mi visto bueno para remitir esa solicitud";
        f.Interpretations.Enqueue(Finish(text)); await f.HandleTurn(text, "reminder");
        Assert.Equal(expires, (await f.Context()).CurrentQuestion!.ExpiresAt);
        f.Now = f.Now.AddMinutes(2); await f.HandleTurn("Confirmar", "expired-consent");
        Assert.Empty(f.Orders); Assert.Contains("Sede Concepcion", f.Sent["expired-consent"].Content);
        await f.HandleTurn("Confirmar", "fresh-consent"); Assert.Single(f.Orders);
    }

    [Theory]
    [InlineData("¿Qué implica remitir esa solicitud al negocio?")]
    [InlineData("No quiero que remitan esa solicitud")]
    [InlineData("Remítanlo únicamente si pueden garantizar entrega hoy")]
    [InlineData("Todavía estoy decidiendo si corresponde enviarlo")]
    public async Task An_unsafe_semantic_submit_label_cannot_manufacture_consent(string text)
    {
        var f = await Reviewed(); var before = (await f.Context()).OrderDraftItems.Select(i => (i.ProductId, i.Quantity)).ToArray();
        // Even an overly eager extraction is not evidence of consent. Literal
        // inclusion of a sentence cannot prove its polarity or conditionality.
        f.Interpretations.Enqueue(Finish(text));
        await f.HandleTurn(text, "non-consent");
        Assert.Empty(f.Orders);
        Assert.Equal(before, (await f.Context()).OrderDraftItems.Select(i => (i.ProductId, i.Quantity)).ToArray());
    }

    [Fact]
    public async Task A_quantity_correction_invalidates_review_and_does_not_submit()
    {
        var f = await Reviewed(); var oldStep = (await f.Context()).CurrentStep;
        const string text = "En ese resumen deja tres unidades de pan integral";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.Correction,
            OrderAction = OrderTurnKind.Edit, Items = [new("Pan integral", "tres unidades de pan integral", OrderEditKind.Set, 3, "tres")],
            Evidence = [new("Act", text, nameof(DialogueAct.Correction))] });
        await f.HandleTurn(text, "edit-quantity");
        Assert.Equal(3, (await f.Context()).OrderDraftItems.Single(i => i.ProductId == "product-a").Quantity);
        Assert.NotEqual(oldStep, (await f.Context()).CurrentStep);
        Assert.StartsWith("ORDER_REVIEW:", (await f.Context()).CurrentStep); Assert.Empty(f.Orders);
        await f.HandleTurn("Confirmar", "old-review-consent"); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("license")]
    [InlineData("location")]
    public async Task A_changed_quote_or_license_blocks_even_existing_bounded_consent(string change)
    {
        var f = await Reviewed(); var oldStep = (await f.Context()).CurrentStep;
        if (change == "price") f.Products[0].PriceMinorUnits = 999;
        if (change == "license") f.Modules.Remove("ORDERS");
        if (change == "location") {
            var state = await f.Context(); state.SelectedLocationId = "sede-b";
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        }
        await f.HandleTurn("Confirmar", "stale-consent");
        Assert.Empty(f.Orders);
        Assert.Equal(new[] { 2, 1 }, (await f.Context()).OrderDraftItems.Select(i => i.Quantity));
        if (change == "price") Assert.NotEqual(oldStep, (await f.Context()).CurrentStep);
    }

    [Fact]
    public async Task A_context_change_during_semantic_interpretation_is_rejected_by_cas()
    {
        var f = await Reviewed();
        f.DuringInterpretation = async () => {
            var state = await f.Context(); state.SelectedLocationId = "sede-b";
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        };
        const string text = "Doy mi visto bueno para remitir ese resumen";
        f.Interpretations.Enqueue(Finish(text));
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.HandleTurn(text, "concurrent-change"));
        Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Replays_and_a_repeated_consent_text_do_not_create_a_second_order()
    {
        var f = await Reviewed();
        await f.HandleTurn("Confirmar", "consent"); await f.HandleTurn("Confirmar", "consent");
        await f.HandleTurn("Confirmar", "another-inbound");
        Assert.Equal(OrderStatus.PendingReview, Assert.Single(f.Orders.Values).Status);
        Assert.Single(f.OrderWrites.Invocations, i => i.Method.Name == "CreateOrderAsync");
    }

    [Theory]
    [InlineData(ConversationIntent.ProviderUnavailable)]
    [InlineData(ConversationIntent.RateLimited)]
    public async Task A_provider_failure_during_free_authorization_never_submits(ConversationIntent failure)
    {
        var f = await Reviewed(); var step = (await f.Context()).CurrentStep; var calls = f.AiCalls;
        f.Interpretations.Enqueue(new() { Intent = failure });
        await f.HandleTurn("Doy mi visto bueno para que remitan ese resumen", "provider-failure");
        Assert.Empty(f.Orders); Assert.Equal(step, (await f.Context()).CurrentStep); Assert.Equal(calls + 1, f.AiCalls);
        Assert.Equal(new[] { 2, 1 }, (await f.Context()).OrderDraftItems.Select(i => i.Quantity));
        await f.HandleTurn("Confirmar", "explicit-after-provider-failure");
        Assert.Single(f.Orders); Assert.Equal(calls + 1, f.AiCalls);
    }

    [Theory]
    [InlineData("Order")]
    [InlineData("Reservation")]
    public async Task The_real_interpreter_rejects_model_supplied_confirmation_for_both_operations(string intent)
    {
        const string text = "Autorizo tramitar el resumen vigente";
        var router = new Mock<IAiRouter>();
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), text, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Intent = intent, Act = "Confirmation", OrderAction = "Submit",
                Evidence = new[] { new { Slot = "Act", Text = text, NormalizedValue = "Confirmation" } } }));
        var clock = Mock.Of<IClock>(c => c.UtcNow == new DateTime(2026, 10, 26, 15, 0, 0, DateTimeKind.Utc));
        var interpreter = new AiInterpreter(router.Object, clock, Mock.Of<IBusinessProfileRepository>(), NullLogger<AiInterpreter>.Instance);
        var result = await interpreter.InterpretAsync(Guid.NewGuid(), text, intent == "Order" ? "ORDER" : "RESERVATION",
            intent == "Order" ? "ORDER_CONFIRM:server-fingerprint" : "CONFIRM", ["ORDERS", "CATALOG", "RESERVATIONS", "SERVICES"], default);
        Assert.Equal(ConversationIntent.ProviderUnavailable, result.Intent);
        Assert.False(result.Confirmed); Assert.Null(result.OrderProposal); Assert.Null(result.BookingProposal);
    }

    [Fact]
    public async Task Reservation_consent_still_requires_its_current_question_and_cannot_come_from_order_semantics()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var state = await f.Context(); state.OrderDraftItems.Clear(); state.TargetDate = "2026-10-28"; state.TargetTime = "16:00";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("continuemos", "booking-summary");
        Assert.Equal(ExpectedReply.ConfirmReservation, (await f.Context()).CurrentQuestion?.Expected);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        await f.HandleTurn("Confirmar", "booking-consent"); await f.HandleTurn("Confirmar", "booking-consent");
        Assert.Single(f.Bookings); Assert.Empty(f.Orders);
    }
}
