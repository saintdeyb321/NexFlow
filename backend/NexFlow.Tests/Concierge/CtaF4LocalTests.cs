using System.Text.Json;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Composition;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class CtaF4LocalTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Empty_global_product_catalog_answers_the_question_before_resuming_the_intact_booking()
    {
        var f = await CtaGate0Tests.PausedOrder(); await f.HandleTurn("En Concepción, limpieza dental", "details");
        f.Products.Clear(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        var paused = JsonSerializer.Serialize((await f.Context()).Focus!.Suspended); var ai = f.AiCalls;
        await f.HandleTurn("¿Qué productos tienen?", "empty");
        output.WriteLine(f.Sent["empty"].Content);
        Assert.Equal("En este momento no hay productos activos publicados en el catálogo del negocio.\n\n"
            + "Busquemos un día que te acomode.\n\n¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.", f.Sent["empty"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal(paused, JsonSerializer.Serialize((await f.Context()).Focus!.Suspended));
        Assert.Equal(ai, f.AiCalls); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData(false, null, "No tengo una opción activa registrada que coincida con tu consulta.")]
    [InlineData(true, "sede-a", "En este momento no hay productos activos publicados en el catálogo de la sede consultada.")]
    public void Empty_catalog_scope_does_not_become_a_claim_about_stock_or_another_site(bool broad, string? site, string expected)
        => Assert.Equal(expected, OfferingResponseFormatter.Format(new(Guid.NewGuid(), "read", null, "PRODUCT", OfferingQuestion.Facts,
            Array.AsReadOnly(Array.Empty<VerifiedOffering>()), broad, site)));

    [Fact]
    public async Task Unified_conversation_binds_each_question_and_revision_and_commits_only_one_order_after_separate_consent()
    {
        var f = CtaGate0Tests.Create(); f.Products[0].Name = "Hilo dental"; var index = 0;
        async Task Turn(string text, AiInterpretation? semantic = null)
        {
            var source = "f4-" + ++index;
            f.InterpretationOverride = semantic == null ? null : () => Task.FromResult(semantic);
            await f.HandleTurn(text, source); var state = await f.Context();
            Assert.Equal(f.Workspace, state.CurrentQuestion!.WorkspaceId); Assert.Equal("chat", state.CurrentQuestion.ConversationId);
            if (state.Focus != null) {
                Assert.Equal(state.Focus.Active.Id, state.CurrentQuestion.OperationId);
                Assert.Equal(state.Focus.Active.ActivationRevision, state.CurrentQuestion.ActivationRevision);
            }
            Assert.Empty(f.Bookings);
            if (text != "Confirmar") Assert.Empty(f.Orders);
            output.WriteLine(JsonSerializer.Serialize(new { Source = source, Text = text, Reply = f.Sent[source].Content,
                state.CurrentGoal, state.CurrentQuestion.Expected, state.CurrentQuestion.OperationId, state.CurrentQuestion.ActivationRevision,
                state.OrderDraftItems, Orders = f.Orders.Count, Reservations = f.Bookings.Count, f.AiCalls }));
        }
        AiInterpretation Meaning(string text, ConversationIntent intent, DialogueAct act) => new() {
            Intent = intent, Act = act, Evidence = [new("Act", text, act.ToString())] };
        await Turn("¿Qué productos tienen?"); await Turn("¿Cuánto cuesta Hilo dental?");
        var referenced = await f.Context();
        output.WriteLine(JsonSerializer.Serialize(new { Offering = OfferingReference.Read(f.Workspace, referenced),
            CurrentQuestionValid = ConversationQuestion.Current(f.Workspace, referenced, f.Now) != null }));
        const string buy = "Me apuntas dos de ese"; var purchase = Meaning(buy, ConversationIntent.Order, DialogueAct.StartOperation);
        purchase.UseOfferingReference = true; purchase.OrderAction = OrderTurnKind.Edit;
        purchase.Items = [new("Hilo dental", "dos de ese", OrderEditKind.Add, 2, "dos", "ese")];
        await Turn(buy, purchase); Assert.Equal(2, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        const string switchText = "Preparemos también una reserva";
        await Turn(switchText, Meaning(switchText, ConversationIntent.Reservation, DialogueAct.StartOperation));
        var orderId = (await f.Context()).Focus!.Suspended!.Identity.Id;
        await Turn("En Concepción, limpieza dental"); var booking = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await Turn("¿Hay disponibilidad mañana a las 10:00?");
        Assert.Equal(booking, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal("10:00", (await f.Context()).Information!.AvailabilityReference!.Time);
        const string resume = "Retomemos el pedido que dejamos preparado";
        await Turn(resume, Meaning(resume, ConversationIntent.Order, DialogueAct.Resume));
        var current = await f.Context(); Assert.Equal(orderId, current.Focus!.Active.Id); Assert.Equal(2, current.Focus.Active.ActivationRevision);
        var paused = JsonSerializer.Serialize(current.Focus.Suspended);
        const string reduce = "Reduce uno, no lo quites"; var reduction = Meaning(reduce, ConversationIntent.Order, DialogueAct.Correction);
        reduction.UseOfferingReference = true; reduction.OrderAction = OrderTurnKind.Edit;
        reduction.Items = [new("Hilo dental", "uno", OrderEditKind.Decrease, 1, "uno")];
        await Turn(reduce, reduction); Assert.Equal(1, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        await Turn("Reconsidera lo que dije", new() { Intent = ConversationIntent.ProviderUnavailable });
        Assert.Equal(1, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        const string send = "Envía solicitud"; var submit = Meaning(send, ConversationIntent.Order, DialogueAct.Resume); submit.OrderAction = OrderTurnKind.Submit;
        await Turn(send, submit); Assert.Equal(ExpectedReply.ConfirmOrder, (await f.Context()).CurrentQuestion!.Expected);
        await Turn("De acuerdo."); Assert.Empty(f.Orders);
        // The H06 acknowledgement invalidates no data and supplies no transaction consent.
        await Turn(send, submit); var revision = (await f.Context()).CurrentQuestion!;
        await Turn("Confirmar"); Assert.Equal(OrderStatus.PendingReview, Assert.Single(f.Orders.Values).Status);
        Assert.Equal(paused, JsonSerializer.Serialize((await f.Context()).Focus!.Suspended));
        var calls = f.AiCalls; await f.HandleTurn("Confirmar", "f4-" + index);
        Assert.Equal(calls, f.AiCalls); Assert.Single(f.Orders); Assert.Empty(f.Bookings);
        Assert.Equal(orderId, revision.OperationId); Assert.Equal(2, revision.ActivationRevision);
    }
}
