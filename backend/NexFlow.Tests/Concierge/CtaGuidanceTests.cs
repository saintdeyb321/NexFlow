using System.Diagnostics;
using System.Text.Json;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

// Synthetic catalogs and injected interpretations: not provider/WhatsApp certification.
public sealed class CtaGuidanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, "dental-demo")]
    [InlineData(1, "tienda")]
    [InlineData(2, "salon")]
    [InlineData(3, "restaurante")]
    [InlineData(4, "referencia-ambigua")]
    [InlineData(5, "precio-nuevo")]
    [InlineData(6, "licencia-revocada")]
    [InlineData(7, "catalogo-vacio")]
    [InlineData(8, "ia-indisponible")]
    [InlineData(9, "presupuesto-agotado")]
    [InlineData(10, "referencia-vencida")]
    [InlineData(11, "atencion-humana")]
    public async Task Twelve_complete_conversations_keep_one_current_next_step_and_verified_effects(int scenario, string label)
    {
        var f = CtaGate0Tests.Create(); var turns = new List<object>(); var index = 0;
        var name = scenario switch { 0 => "Hilo dental", 2 => "Champú", 3 => "Menú vegetal", _ => "Pan integral" };
        f.Products[0].Name = name;
        async Task Step(string text, AiInterpretation? semantic = null)
        {
            var source = $"{label}-{++index}"; var ai = f.AiCalls; var writes = f.Firestore.CommitRpcCalls;
            var reads = f.Firestore.ReadRpcCalls; var watch = Stopwatch.StartNew();
            f.InterpretationOverride = semantic == null ? null : () => Task.FromResult(semantic);
            await f.HandleTurn(text, source); watch.Stop(); var state = await f.Context();
            turns.Add(new { Client = text, Reply = f.Sent.GetValueOrDefault(source)?.Content ?? "[sin respuesta automática]",
                Active = state.CurrentGoal, Suspended = state.Focus?.Suspended?.Identity.Kind.ToString(),
                state.CurrentStep, Question = state.CurrentQuestion?.Expected.ToString(),
                Items = state.OrderDraftItems.Select(i => new { i.ProductName, i.Quantity }).ToArray(),
                state.SelectedLocationId, state.SelectedServiceId, state.TargetDate, state.TargetTime, state.RealCustomerName,
                Orders = f.Orders.Count, Reservations = f.Bookings.Count, Ai = f.AiCalls - ai,
                CasCommits = f.Firestore.CommitRpcCalls - writes, Reads = f.Firestore.ReadRpcCalls - reads,
                FixtureMs = watch.ElapsedMilliseconds, Clarifications = state.ClarificationAttempts });
        }
        AiInterpretation Semantic(string text, ConversationIntent intent, DialogueAct act) => new() {
            Intent = intent, Act = act, Evidence = [new("Act", text, act.ToString())] };
        AiInterpretation Item(string text, OrderEditKind kind, int quantity, string literal) {
            var value = Semantic(text, ConversationIntent.Order, DialogueAct.Correction);
            value.OrderAction = OrderTurnKind.Edit; value.Items = [new(name, text, kind, quantity, literal)]; return value;
        }
        await Step("¿Qué productos tienen?"); await Step("Quiero hacer un pedido");
        var itemText = $"Solicito dos unidades de {name}";
        var items = Item(itemText, OrderEditKind.Add, 2, "dos"); items.Act = DialogueAct.RequestedData;
        items.Evidence = [new("Act", itemText, nameof(DialogueAct.RequestedData))]; await Step(itemText, items);
        const string switchText = "Preparemos también una cita";
        await Step(switchText, Semantic(switchText, ConversationIntent.Reservation, DialogueAct.StartOperation));
        var transition = f.Sent[$"{label}-{index}"].Content;
        await Step("En Concepción, limpieza dental");
        if (scenario == 0) { await Step("mañana a las 10:00"); await Step("Elena Flores"); }
        if (scenario == 5) f.Products[0].PriceMinorUnits = 350;
        if (scenario == 6) f.Modules.Remove("ORDERS");
        if (scenario == 7) { f.Products.Clear(); await Step("¿Qué productos tienen?"); }
        const string resume = "Retomemos mi selección de productos";
        await Step(resume, Semantic(resume, ConversationIntent.Order, DialogueAct.Resume));
        if (scenario == 0) {
            var correction = $"Ajusta {name} a uno"; await Step(correction, Item(correction, OrderEditKind.Set, 1, "uno"));
            var send = Semantic("Enviar la solicitud", ConversationIntent.Order, DialogueAct.Resume); send.OrderAction = OrderTurnKind.Submit;
            await Step("Enviar la solicitud", send); await Step("Confirmar");
            const string booking = "Retomemos la cita que dejamos preparada"; await Step(booking, Semantic(booking, ConversationIntent.Reservation, DialogueAct.Resume));
            await Step("Continuemos"); await Step("Confirmar");
        }
        else if (scenario == 4) {
            await Step("Quiero añadir un queque");
            var ambiguous = Semantic("Deja uno de esos", ConversationIntent.General, DialogueAct.SemanticAmbiguity);
            ambiguous.RequiresClarification = true; await Step("Deja uno de esos", ambiguous);
            await Step("Descartar suspendido"); await Step("No");
        }
        else if (scenario is 8 or 9) await Step("Retoca lo que habíamos visto", new() {
            Intent = scenario == 8 ? ConversationIntent.ProviderUnavailable : ConversationIntent.RateLimited });
        else if (scenario == 10) {
            f.Now = f.Now.AddMinutes(11); var expired = Semantic("Rectifica uno de ese", ConversationIntent.Order, DialogueAct.Correction);
            expired.UseOfferingReference = true; expired.OrderAction = OrderTurnKind.Edit;
            expired.Items = [new(name, "uno de ese", OrderEditKind.Set, 1, "uno", "ese")]; await Step("Rectifica uno de ese", expired);
        }
        else if (scenario == 11) await Step("asesor");
        else if (scenario is 1 or 2 or 3) { await Step("Descartar activo"); await Step("Sí"); }
        // Full messages and state/effects are retained even when the presentation gate fails.
        output.WriteLine("CTA_TRANSCRIPT:" + JsonSerializer.Serialize(new { Scenario = label, Turns = turns }));
        if (scenario == 0) { Assert.Single(f.Orders); Assert.Single(f.Bookings); Assert.Equal(OrderStatus.PendingReview, f.Orders.Values.Single().Status); }
        else { Assert.Empty(f.Orders); Assert.Empty(f.Bookings); }
        Assert.All(f.Composed, c => Assert.False(c.UsedAi));
        if (scenario is 8 or 9) Assert.Equal(
            "No he cambiado tu solicitud ni la he enviado. Conservamos tus productos y cantidades.\n\n"
            + "Puedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen. Si necesitas ayuda de una persona, escribe «asesor».\n\n"
            + "Tu pedido está en preparación y tu reserva sigue guardada. Puedes pedir «Retomar la reserva»; al volver comprobaremos de nuevo los datos.",
            f.Sent[$"{label}-{index}"].Content);
        if (scenario == 6) Assert.Equal("Este negocio no tiene habilitados los pedidos.", f.Sent[$"{label}-{index}"].Content);
        Assert.Equal(ExpectedTransition, transition);
    }

    private const string ExpectedTransition = "Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.\n\n"
        + "Podemos atenderte en estas sedes:\n\n1. Sede Concepcion\n2. Sede San Jeronimo\n\n¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».";
}
