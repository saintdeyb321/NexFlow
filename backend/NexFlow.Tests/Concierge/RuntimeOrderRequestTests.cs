using Moq;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class RuntimeOrderRequestTests
{
    private static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true, intelligentCommerce: true);

    [Theory]
    [InlineData("¿Se puede hacer un pedido?")]
    [InlineData("¿Venden por WhatsApp?")]
    [InlineData("¿Cómo encargo productos?")]
    public async Task Mvp_order_capability_is_information_followed_by_explicit_preparation(string text)
    {
        var f = Create();
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.OperationInformation,
            Evidence = [new("Act", text, nameof(DialogueAct.OperationInformation))] });
        await f.HandleTurn(text, "capability");
        Assert.Contains("solicitud de pedido", f.Sent["capability"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
        await f.HandleTurn("quiero hacer un pedido", "explicit-start");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal); Assert.Empty((await f.Context()).OrderDraftItems);
        Assert.Empty(f.Orders); Assert.Contains("cantidades", f.Sent["explicit-start"].Content);
    }

    [Theory]
    [InlineData("ORDERS", "no tiene habilitados los pedidos")]
    [InlineData("CATALOG", "No hay información de productos disponible en este momento.")]
    [InlineData("services-only", "no tiene habilitados los pedidos")]
    [InlineData("empty", "No hay productos activos")]
    [InlineData("location", "No hay productos activos")]
    public async Task Mvp_order_capability_reflects_license_and_current_location_catalog(string unavailable, string expected)
    {
        var f = Create();
        if (unavailable == "services-only") f.Modules.IntersectWith(["SERVICES", "RESERVATIONS"]);
        if (unavailable is "ORDERS" or "CATALOG") f.Modules.Remove(unavailable);
        if (unavailable == "empty") f.Products.Clear();
        if (unavailable == "location")
        {
            foreach (var p in f.Products) { p.LocationScope = "SPECIFIC"; p.LocationIds = ["sede-b"]; }
            var state = await f.Context(); state.SelectedLocationId = "sede-a";
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        }
        const string text = "¿Se puede hacer un pedido?";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.OperationInformation,
            Evidence = [new("Act", text, nameof(DialogueAct.OperationInformation))] });
        await f.HandleTurn(text, "no-capability");
        Assert.Contains(expected, f.Sent["no-capability"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
        if (unavailable == "CATALOG") f.OfferingReads.Verify(o => o.GetProductsAsync(f.Workspace,
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.All(f.OfferingReads.Invocations, i => Assert.Equal(f.Workspace, i.Arguments[0]));
    }

    [Fact]
    public async Task Mvp_capability_revocation_and_booking_interruption_preserve_existing_draft()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        const string text = "¿Se puede hacer un pedido?";
        foreach (var source in new[] { "before-revocation", "after-revocation" })
        {
            f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.OperationInformation,
                Evidence = [new("Act", text, nameof(DialogueAct.OperationInformation))] });
            await f.HandleTurn(text, source);
            Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
            if (source == "before-revocation") Assert.Contains("solicitud de pedido", f.Sent[source].Content);
            else Assert.Contains("no tiene habilitados los pedidos", f.Sent[source].Content);
            f.Modules.Remove("ORDERS");
        }
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mvp_information_proposal_cannot_prepare_or_submit_an_order(bool backed)
    {
        var f = Create(); const string text = "¿Cómo coordino una compra?";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.OperationInformation,
            Evidence = backed ? [new("Act", text, nameof(DialogueAct.OperationInformation))] : [] });
        await f.HandleTurn(text, "query-only");
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
        if (backed) Assert.Contains("solicitud de pedido", f.Sent["query-only"].Content);
        else Assert.DoesNotContain("solicitud de pedido", f.Sent["query-only"].Content);
    }

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
