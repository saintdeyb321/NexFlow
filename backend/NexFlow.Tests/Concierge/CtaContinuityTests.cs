using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class CtaContinuityTests
{
    private static async Task<AssistantConversationTests.Fixture> Order(bool two = false)
    {
        var f = CtaGate0Tests.Create(); await f.HandleTurn("Quiero hacer un pedido", "start");
        await f.HandleTurn(two ? "dos panes integrales y un queque" : "dos panes integrales", "items");
        await CtaGate0Tests.Turn(f, "Quisiera preparar una cita", "switch", ConversationIntent.Reservation, DialogueAct.StartOperation);
        await f.HandleTurn("En Concepción, limpieza dental", "details");
        await CtaGate0Tests.Turn(f, "Volvamos a mi selección de compra", "resume", ConversationIntent.Order, DialogueAct.Resume);
        return f;
    }

    internal static async Task Edit(AssistantConversationTests.Fixture f, string text, string source, AiOrderItem item, bool referenced = false)
    {
        f.InterpretationOverride = () => Task.FromResult(new AiInterpretation { Intent = ConversationIntent.Order,
            Act = DialogueAct.Correction, OrderAction = OrderTurnKind.Edit, UseOfferingReference = referenced,
            Items = [item], Evidence = [new("Act", text, nameof(DialogueAct.Correction))] });
        await f.HandleTurn(text, source);
    }

    [Theory]
    [InlineData("Rectifica la selección: solo uno de ese", "uno de ese", "uno", "ese", OrderEditKind.Set, 1)]
    [InlineData("Reduce la selección en uno de ese", "uno de ese", "uno", "ese", OrderEditKind.Decrease, 1)]
    [InlineData("Ajusta la selección: mejor tres de ese", "tres de ese", "tres", "ese", OrderEditKind.Set, 3)]
    public async Task Natural_absolute_and_incremental_changes_after_resume_preserve_the_other_draft(string text, string span,
        string quantity, string reference, OrderEditKind kind, int expected)
    {
        var f = await Order(); var paused = JsonSerializer.Serialize((await f.Context()).Focus!.Suspended);
        await Edit(f, text, "edit", new("Pan integral", span, kind, expected == 3 ? 3 : 1, quantity, reference), true);
        Assert.Equal(expected, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        Assert.Equal(paused, JsonSerializer.Serialize((await f.Context()).Focus!.Suspended));
        var calls = f.AiCalls; await f.HandleTurn(text, "edit"); Assert.Equal(calls, f.AiCalls);
        Assert.Equal(expected, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_current_order_ordinal_resolves_only_the_displayed_line_and_never_the_suspended_service()
    {
        var f = await Order(two: true);
        await Edit(f, "Rectifica uno del primero", "ordinal", new("Pan integral", "uno del primero", OrderEditKind.Set, 1, "uno", "primero", 1), true);
        Assert.Equal(new[] { 1, 1 }, (await f.Context()).OrderDraftItems.Select(i => i.Quantity));
        Assert.Equal("service-a", (await f.Context()).Focus!.Suspended!.ServiceId);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("removed")]
    [InlineData("forged-index")]
    public async Task Unverified_references_leave_both_preparations_intact(string failure)
    {
        var f = await Order(two: failure == "ambiguous" || failure == "forged-index");
        if (failure == "expired") f.Now = f.Now.AddMinutes(11);
        if (failure == "revoked") f.Modules.Remove("ORDERS");
        if (failure == "removed") f.Products.Clear();
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        var paused = JsonSerializer.Serialize((await f.Context()).Focus!.Suspended);
        await Edit(f, "Rectifica uno de ese", "bad", new("Pan integral", "uno de ese", OrderEditKind.Set, 1, "uno", "ese", failure == "forged-index" ? 1 : null), true);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal(paused, JsonSerializer.Serialize((await f.Context()).Focus!.Suspended)); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_mutable_intents_or_a_provider_failure_never_apply_a_partial_change(bool mixed)
    {
        var f = await Order(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.InterpretationOverride = () => Task.FromResult(mixed ? new AiInterpretation { Intent = ConversationIntent.General,
            RequiresClarification = true, Items = [new("Pan integral", "uno", OrderEditKind.Set, 1, "uno")],
            Directive = ConversationDirective.ChangeLocation } : new AiInterpretation { Intent = ConversationIntent.ProviderUnavailable });
        await f.HandleTurn(mixed ? "Reduce mi compra y cambia también la cita" : "Reformula mi selección", "uncertain");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 2)]
    public async Task Incremental_reductions_use_the_current_quantity_and_never_create_negative_items(int decrement, int expected)
    {
        var f = await Order();
        var text = $"Reduce {decrement} de ese";
        await Edit(f, text, "decrease", new("Pan integral", $"{decrement} de ese", OrderEditKind.Decrease, decrement, decrement.ToString(), "ese"), true);
        Assert.Equal(expected, (await f.Context()).OrderDraftItems.Sum(i => i.Quantity)); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("{invalid-json", false)]
    [InlineData("{\"Intent\":\"Order\",\"Confirmed\":true}", false)]
    [InlineData("timeout", false)]
    [InlineData("valid", true)]
    public async Task The_real_interpreter_contract_bounds_reference_evidence_and_rejects_model_consent(string response, bool valid)
    {
        var f = await Order(); var router = new Mock<IAiRouter>();
        var setup = router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()));
        if (response == "timeout") setup.ThrowsAsync(new TimeoutException("Synthetic provider timeout"));
        else setup.ReturnsAsync(valid ? """{"Intent":"Order","Act":"Correction","OrderAction":"Edit","UseOfferingReference":true,"Items":[{"Product":"Pan integral","Text":"uno de ese","Kind":"Decrease","Quantity":1,"QuantityText":"uno","ReferenceText":"ese"}],"Evidence":[{"Slot":"Act","Text":"Reduce uno de ese","NormalizedValue":"Correction"}]}""" : response);
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => f.Now);
        var interpreter = new AiInterpreter(router.Object, clock.Object, f.ProfileReads.Object, NullLogger<AiInterpreter>.Instance);
        f.InterpretationOverride = async () => {
            var state = await f.Context(); var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
            var result = await interpreter.InterpretAsync("Reduce uno de ese", DialogueTurnContext.Capture(f.Workspace, "provider", state,
                TimeZoneInfo.ConvertTime(new DateTimeOffset(f.Now), zone), zone, f.Modules) with { SupportsFocusSwitch = true }, default);
            Assert.False(result.Confirmed); Assert.Null(result.OrderProposal); Assert.Null(result.FocusProposal); return result;
        };
        await f.HandleTurn("Reduce uno de ese", "provider");
        Assert.Equal(valid ? 1 : 2, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        router.Verify(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }
}
