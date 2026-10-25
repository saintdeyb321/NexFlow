using Moq;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class RuntimeOrderRequestTests
{
    private static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true, intelligentCommerce: true);

    [Fact]
    public async Task Empty_start_is_persistent_replay_safe_and_information_does_not_discard_it()
    {
        var f = Create();
        await f.HandleTurn("Quiero hacer un pedido", "start");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal);
        Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
        Assert.Contains("productos", f.Sent["start"].Content, StringComparison.OrdinalIgnoreCase);
        await f.HandleTurn("Quiero hacer un pedido", "start");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal); Assert.Empty((await f.Context()).OrderDraftItems);
        await f.HandleTurn("¿Cuánto cuesta pan integral?", "price");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal); Assert.Contains("2.50", f.Sent["price"].Content);
        await f.HandleTurn("tres panes integrales y dos queques", "items");
        Assert.Equal(2, (await f.Context()).OrderDraftItems.Count); Assert.Empty(f.Orders);
        await f.HandleTurn("Mejor cuatro panes", "correct");
        Assert.Equal(4, (await f.Context()).OrderDraftItems.Single(i => i.ProductId == "product-a").Quantity);
        await f.HandleTurn("envía el pedido", "submit"); await f.HandleTurn("envía el pedido", "submit");
        Assert.Equal(OrderStatus.PendingReview, Assert.Single(f.Orders.Values).Status);
        Assert.Single(f.OrderWrites.Invocations, i => i.Method.Name == "CreateOrderAsync");
        Assert.Contains("revisará", f.Sent["submit"].Content); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_catalog_or_revoked_license_cannot_start_a_request(bool revoked)
    {
        var f = Create(); if (revoked) f.Modules.Remove("ORDERS"); else f.Products.Clear();
        await f.HandleTurn("Quiero hacer un pedido", "start");
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Orders);
        Assert.Empty((await f.Context()).OrderDraftItems);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Semantic_empty_start_requires_literal_start_evidence(bool backed)
    {
        var f = Create();
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.StartOperation,
            Evidence = backed ? [new("Act", "armemos una solicitud", nameof(DialogueAct.StartOperation))] : [] });
        await f.HandleTurn("armemos una solicitud", "semantic");
        Assert.Equal(backed ? "ORDER" : null, (await f.Context()).CurrentGoal);
        Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Unknown_delivery_of_the_start_reply_does_not_repeat_it_or_create_items()
    {
        var f = Create(); f.DeliveryStatus = MessageStatus.UnknownDelivery;
        await f.HandleTurn("Quiero hacer un pedido", "uncertain");
        await f.HandleTurn("Quiero hacer un pedido", "uncertain");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal);
        Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
        Assert.Equal(MessageStatus.UnknownDelivery, f.Sent["uncertain"].Status);
        Assert.Single(f.Outbound.Invocations, i => i.Method.Name == "SendMessageAsync");
    }

    [Fact]
    public async Task Empty_start_keeps_workspace_identity_and_human_takeover_blocks_later_edits()
    {
        var f = Create(); var other = Create();
        await f.HandleTurn("Quiero hacer un pedido", "start");
        Assert.Null((await other.Context()).CurrentGoal);
        await f.HandleTurn("quiero hablar con una persona", "human");
        await f.HandleTurn("tres panes integrales", "late-items");
        Assert.Equal(1, f.Handoffs); Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
        Assert.All(f.OfferingReads.Invocations, i => Assert.Equal(f.Workspace, i.Arguments[0]));
    }

    [Fact]
    public async Task Empty_preparation_cannot_be_submitted_and_is_discarded_only_after_confirmation()
    {
        var f = Create(); await f.HandleTurn("Quiero hacer un pedido", "start");
        await f.HandleTurn("envía el pedido", "empty-submit");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal); Assert.Empty(f.Orders);
        await f.HandleTurn("cancelar pedido", "cancel");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal);
        await f.HandleTurn("sí", "discard");
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Orders);
    }
}
