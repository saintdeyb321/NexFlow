using Moq;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class VirtualSecretaryB5Tests(ITestOutputHelper output)
{
    private static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true, intelligentCommerce: true)
        { Now = new(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc) };

    [Theory]
    [InlineData("Me separas dos cajas", "dos cajas", "dos", 2)]
    [InlineData("Quiero llevar tres", "tres", "tres", 3)]
    [InlineData("¿Me apartarías dos cajas?", "dos cajas", "dos", 2)]
    public async Task Natural_order_start_uses_a_current_product_reference_and_literal_quantity(string text, string span, string number, int quantity)
    {
        var f = Create(); f.Products[0].Name = "Caja de alfajores";
        await f.HandleTurn("¿Cuánto cuesta Caja de alfajores?", "product");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.StartOperation,
            OrderAction = OrderTurnKind.Edit, UseOfferingReference = true,
            Items = [new("Caja de alfajores", span, OrderEditKind.Add, quantity, number)],
            Evidence = [new("Act", text.TrimStart('¿'), nameof(DialogueAct.StartOperation))] });
        await f.HandleTurn(text, "natural"); await f.HandleTurn(text, "natural");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal);
        var item = Assert.Single((await f.Context()).OrderDraftItems); Assert.Equal("product-a", item.ProductId); Assert.Equal(quantity, item.Quantity);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("COLLECT_LOCATION")]
    [InlineData("COLLECT_SERVICE")]
    [InlineData("COLLECT_DATE")]
    [InlineData("COLLECT_TIME")]
    public async Task WhatsApp_collectors_have_one_question_and_suggested_answers_in_quotes(string step)
    {
        var f = Create(); await f.SeedDraft("RESERVATION"); var state = await f.Context();
        state.CurrentStep = step;
        if (step == "COLLECT_LOCATION") { state.SelectedLocationId = null; state.SelectedServiceId = null; }
        if (step == "COLLECT_SERVICE") state.SelectedServiceId = null;
        if (step == "COLLECT_TIME") state.TargetDate = "2026-10-28";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("continuemos", "prompt"); var response = f.Sent["prompt"].Content;
        Assert.Equal(1, response.Count(c => c == '¿')); Assert.Contains("\n\n", response);
        Assert.Contains("«", response); Assert.Contains("»", response);
        Assert.DoesNotContain("Mostrando", response); Assert.DoesNotContain("Sedes disponibles:", response);
        Assert.DoesNotContain("Servicios reservables disponibles en esta sede:", response); Assert.Empty(f.Bookings);
        output.WriteLine(response);
    }

    [Fact]
    public async Task Ambiguous_semantic_result_never_applies_a_proposed_edit()
    {
        var f = Create(); await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        const string text = "No estoy segura de cómo seguir con eso";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, RequiresClarification = true,
            Directive = ConversationDirective.ChangeLocation });
        await f.HandleTurn(text, "uncertain");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal(1, f.Sent["uncertain"].Content.Count(c => c == '¿'));
        Assert.Empty((await f.Context()).PendingDialogueSlots); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("expired")] [InlineData("foreign")] [InlineData("broad")]
    [InlineData("new-question")] [InlineData("quantity")] [InlineData("unknown-product")]
    [InlineData("ambiguous-product")] [InlineData("revoked")]
    public async Task Natural_references_fail_closed_without_valid_identity_quantity_catalog_and_license(string reason)
    {
        var f = Create(); f.Products[0].Name = "Caja de alfajores";
        await f.HandleTurn("¿Cuánto cuesta Caja de alfajores?", "price"); var state = await f.Context();
        var reference = OfferingReference.Read(f.Workspace, state)!;
        if (reason == "expired") f.Now = f.Now.AddMinutes(11);
        if (reason == "foreign") (reference with { WorkspaceId = Guid.NewGuid() }).Remember(state);
        if (reason == "broad") (reference with { Broad = true }).Remember(state);
        if (reason == "new-question") ConversationQuestion.Remember(f.Workspace, "new", state, ExpectedReply.Information, ConversationIntent.Location, "¿Qué ubicación necesitas?", f.Now);
        if (reason == "ambiguous-product") foreach (var product in f.Products.Take(2)) product.Metadata["aliases"] = new[] { "cajas" };
        if (reason == "revoked") f.Modules.Remove("ORDERS");
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        var span = reason == "unknown-product" ? "tres desconocidos" : reason == "ambiguous-product" ? "tres cajas" : "tres";
        var text = "Solicitaría " + span;
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.StartOperation,
            OrderAction = OrderTurnKind.Edit, UseOfferingReference = true,
            Items = [new("Caja de alfajores", span, OrderEditKind.Add, reason == "quantity" ? 4 : 3, "tres")],
            Evidence = [new("Act", text, nameof(DialogueAct.StartOperation))] });
        await f.HandleTurn(text, "unsafe-reference");
        Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_pickup_question_answers_without_repeating_collected_items_or_committing()
    {
        var f = Create(); await f.HandleTurn("Quiero hacer un pedido", "start"); await f.HandleTurn("dos panes integrales", "items");
        Assert.Single((await f.Context()).OrderDraftItems);
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        const string text = "¿Me lo preparan para recoger?";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.OperationInformation,
            Evidence = [new("Act", text.TrimStart('¿'), nameof(DialogueAct.OperationInformation))] });
        await f.HandleTurn(text, "pickup"); var response = f.Sent["pickup"].Content;
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Contains("recojo", response); Assert.Contains("coordina", response); Assert.Equal(1, response.Count(c => c == '¿'));
        Assert.DoesNotContain("¿Qué productos y cantidades", response); Assert.Empty(f.Orders);
    }

    private async Task Speak(AssistantConversationTests.Fixture f, string text, string source)
    {
        await f.HandleTurn(text, source); var response = f.Sent[source].Content;
        output.WriteLine("Cliente: " + text + "\nNexFlow: " + response + "\n");
        Assert.True(response.Length < 3000); Assert.InRange(response.Count(c => c == '¿'), 0, 1);
        Assert.DoesNotContain("Mostrando", response); Assert.DoesNotContain("{\"", response);
    }

    [Fact]
    public async Task Transcript_booking_with_partial_edit_and_separate_consent()
    {
        var f = Create(); f.Services[0].Name = "Evaluación general"; f.Services[0].PriceMinorUnits = 6000;
        await Speak(f, "Soy Elena Flores, quiero reservar evaluación general mañana a las cuatro de la tarde en Concepción", "reserve");
        Assert.Empty(f.Bookings);
        await Speak(f, "Me gustaría atenderme en Sanjeronimo sin cambiar lo demás", "site");
        var state = await f.Context(); Assert.Equal("sede-b", state.SelectedLocationId); Assert.Equal("service-a", state.SelectedServiceId);
        Assert.Equal("2026-10-28", state.TargetDate); Assert.Equal("16:00", state.TargetTime); Assert.Equal("Elena Flores", state.RealCustomerName);
        Assert.Empty(f.Bookings);
        await Speak(f, "¿Cuánto dura?", "duration");
        Assert.Contains("30 min", f.Sent["duration"].Content); Assert.Empty(f.Bookings);
        await Speak(f, "sigamos", "review-again");
        await Speak(f, "Sí confirmo", "consent"); await f.HandleTurn("Sí confirmo", "consent");
        Assert.Single(f.Bookings); Assert.Equal("sede-b", f.Bookings.Values.Single().LocationId);
    }

    [Fact]
    public async Task Transcript_services_explain_global_and_site_scope_from_real_fixture_data()
    {
        var f = Create(); f.Services[0].Name = "Evaluación general"; f.Services[0].PriceMinorUnits = 6000;
        f.Services[1].PriceMinorUnits = 12000;
        for (var i = 2; i < 5; i++) f.Services.Add(new NexFlow.Application.Features.Services.DTOs.ServiceDto {
            Id = "service-" + i, Name = "Servicio " + i, RequiresReservation = true, DurationInMinutes = 30, PriceMinorUnits = 7000 });
        f.Services[4].Name = "Restauración simple"; f.Services[4].LocationScope = "SPECIFIC"; f.Services[4].LocationIds = ["sede-b"];
        f.UseCatalogKnowledge();
        await Speak(f, "¿Qué servicios ofrecen y cuáles son sus precios?", "catalog");
        Assert.Equal(0, f.AiCalls);
        Assert.Contains("S/ 60.00", f.Sent["catalog"].Content); Assert.Contains("Restauración simple", f.Sent["catalog"].Content);
        await Speak(f, "Quiero reservar en Concepción", "site");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.ExplainServiceScope });
        await Speak(f, "¿Por qué el catálogo tiene cinco y aquí aparecen cuatro?", "scope");
        Assert.Contains("5 servicios", f.Sent["scope"].Content); Assert.Contains("4 se pueden reservar", f.Sent["scope"].Content);
        Assert.Contains("Sede San Jeronimo", f.Sent["scope"].Content); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Transcript_natural_order_remains_pending_review_until_explicit_consent()
    {
        var f = Create(); f.Products[0].Name = "Caja de alfajores"; f.Products[0].PriceMinorUnits = 1500;
        await Speak(f, "¿Cuánto cuesta Caja de alfajores?", "price");
        const string request = "Me separas dos cajas";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.StartOperation, OrderAction = OrderTurnKind.Edit,
            Items = [new("Caja de alfajores", "dos cajas", OrderEditKind.Add, 2, "dos")],
            Evidence = [new("Act", request, nameof(DialogueAct.StartOperation))] });
        await Speak(f, request, "prepare"); Assert.Empty(f.Orders);
        const string pickup = "¿Me lo preparan para recoger?";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.OperationInformation,
            Evidence = [new("Act", pickup.TrimStart('¿'), nameof(DialogueAct.OperationInformation))] });
        await Speak(f, pickup, "pickup"); Assert.Empty(f.Orders);
        const string finish = "Ya elegí todo, revisemos lo que llevaré";
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.Resume, OrderAction = OrderTurnKind.Submit,
            Evidence = [new("Act", finish, nameof(DialogueAct.Resume))] });
        await Speak(f, finish, "review"); Assert.Empty(f.Orders); Assert.Contains("S/ 30.00", f.Sent["review"].Content);
        await Speak(f, "Sí confirmo", "consent"); await f.HandleTurn("Sí confirmo", "consent");
        Assert.Equal(OrderStatus.PendingReview, Assert.Single(f.Orders.Values).Status);
        Assert.Single(f.OrderWrites.Invocations, i => i.Method.Name == "CreateOrderAsync");
    }

    [Fact]
    public async Task The_suggested_yes_for_a_single_service_selects_it_without_confirming_a_booking()
    {
        var f = Create(); f.Services.RemoveAt(1);
        await f.HandleTurn("Quiero reservar", "start"); await f.HandleTurn("Concepción", "site");
        Assert.Contains("«Sí»", f.Sent["site"].Content);
        await f.HandleTurn("Sí", "service-choice");
        Assert.Equal("service-a", (await f.Context()).SelectedServiceId); Assert.Empty(f.Bookings);
        Assert.Equal("COLLECT_DATE", (await f.Context()).CurrentStep);
    }
}
