using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class CtaGate0Tests
{
    internal static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true, intelligentCommerce: true, contextualFocus: true)
        { Now = new(2026, 10, 31, 14, 0, 0, DateTimeKind.Utc) };

    internal static async Task Turn(AssistantConversationTests.Fixture f, string text, string source, ConversationIntent intent, DialogueAct act)
    {
        f.InterpretationOverride = () => Task.FromResult(new AiInterpretation { Intent = intent, Act = act,
            Evidence = [new("Act", text, act.ToString())] });
        await f.HandleTurn(text, source);
    }

    internal static async Task<AssistantConversationTests.Fixture> PausedOrder(bool endActive = false)
    {
        var f = Create(); await f.HandleTurn("Quiero hacer un pedido", "start");
        await f.HandleTurn("dos panes integrales y un queque", "items");
        await Turn(f, "También quiero hacer una reserva", "switch", ConversationIntent.Reservation, DialogueAct.StartOperation);
        if (endActive) { await f.HandleTurn("Descartar activo", "cancel-active"); await f.HandleTurn("Sí", "yes-active"); }
        return f;
    }

    [Fact]
    public async Task An_expired_suspended_draft_can_be_explicitly_discarded_then_a_new_operation_started()
    {
        var f = await PausedOrder(endActive: true); Assert.Null((await f.Context()).CurrentGoal);
        f.Now = f.Now.AddHours(25);
        await f.HandleTurn("Descartar suspendido", "discard-suspended");
        Assert.NotNull((await f.Context()).Focus!.Suspended);
        await f.HandleTurn("Sí", "discard-consent");
        Assert.Null((await f.Context()).Focus);
        await f.HandleTurn("Quiero hacer un pedido", "new-start");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal); Assert.Empty((await f.Context()).OrderDraftItems);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("¿Dónde están?")]
    [InlineData("¿Qué horarios tienen?")]
    public async Task Fast_information_keeps_verified_conversation_binding_when_only_a_suspended_draft_remains(string text)
    {
        var f = await PausedOrder(endActive: true);
        await f.HandleTurn(text, "fast-query"); var state = await f.Context();
        Assert.Equal("chat", state.CurrentQuestion!.ConversationId);
        Assert.NotNull(ConversationQuestion.Current(f.Workspace, state, f.Now));
        Assert.Equal(ExpectedReply.Information, state.CurrentQuestion.Expected);
        Assert.NotNull(state.Focus!.Suspended); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_new_start_offers_safe_discard_of_the_expired_draft_without_implicitly_starting_or_erasing_it()
    {
        var f = await PausedOrder(endActive: true); f.Now = f.Now.AddHours(25);
        await f.HandleTurn("Quiero hacer un pedido", "fresh-request");
        Assert.Equal(DraftDiscardScope.Suspended, (await f.Context()).Focus!.PendingDiscard!.Scope);
        await f.HandleTurn("No", "keep"); Assert.NotNull((await f.Context()).Focus!.Suspended);
        await f.HandleTurn("Quiero hacer un pedido", "request-again");
        await f.HandleTurn("Sí", "discard"); Assert.Null((await f.Context()).Focus); Assert.Null((await f.Context()).CurrentGoal);
        await f.HandleTurn("Quiero hacer un pedido", "fresh"); Assert.Equal("ORDER", (await f.Context()).CurrentGoal);
        Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Order_response_markers_do_not_cross_focus_or_override_a_fresh_resume()
    {
        var f = await PausedOrder(); var state = await f.Context();
        Assert.Equal("items", state.OrderLastSourceMessageId);
        var old = state.OrderLastResponse;
        f.Products[0].PriceMinorUnits = 350;
        await Turn(f, "Retomemos el pedido", "resume", ConversationIntent.Order, DialogueAct.Resume);
        Assert.Equal("resume", (await f.Context()).OrderLastSourceMessageId);
        Assert.NotEqual(old, (await f.Context()).OrderLastResponse);
        Assert.Contains("17.00", f.Sent["resume"].Content);
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context()); var calls = f.AiCalls;
        await f.HandleTurn("dos panes integrales y un queque", "items");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Equal(calls, f.AiCalls);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("Descartar activo", false, true)]
    [InlineData("Descartar suspendido", true, false)]
    [InlineData("Descartar ambos", false, false)]
    public async Task Discard_scope_never_crosses_preparations_and_always_needs_a_second_message(string choice, bool active, bool paused)
    {
        var f = await PausedOrder(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn(choice, "scope");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        await f.HandleTurn("Sí", "consent"); await f.HandleTurn("Sí", "consent");
        Assert.Equal(active, (await f.Context()).CurrentGoal != null); Assert.Equal(paused, (await f.Context()).Focus?.Suspended != null);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task An_ambiguous_cancel_and_an_expired_answer_cannot_discard_anything()
    {
        var f = await PausedOrder(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("Cancelar", "ambiguous"); await f.HandleTurn("Sí", "not-scope");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.NotNull((await f.Context()).Focus!.Suspended);
        await f.HandleTurn("Descartar ambos", "scope"); f.Now = f.Now.AddMinutes(11);
        await f.HandleTurn("Sí", "expired");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.NotNull((await f.Context()).Focus!.Suspended);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Concurrent_discard_answers_have_one_cas_winner_and_cannot_erase_a_newer_projection()
    {
        var f = await PausedOrder(); await f.HandleTurn("Descartar suspendido", "scope");
        var a = await f.Context(); var b = await f.Context();
        Assert.NotNull(f.Focus.HandleDiscard(f.Workspace, "chat", "yes-a", "Sí", a));
        Assert.NotNull(f.Focus.HandleDiscard(f.Workspace, "chat", "yes-b", "Sí", b));
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, a, default);
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, b, default));
        Assert.Null((await f.Context()).Focus!.Suspended); Assert.Equal("RESERVATION", (await f.Context()).CurrentGoal);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("conversation")]
    [InlineData("human")]
    public async Task A_scoped_discard_never_accepts_a_foreign_or_human_controlled_answer(string changed)
    {
        var f = await PausedOrder(); await f.HandleTurn("Descartar ambos", "scope"); var state = await f.Context();
        if (changed == "human") state.Mode = "Human";
        Assert.Throws<ConcurrencyException>(() => f.Focus.HandleDiscard(changed == "workspace" ? Guid.NewGuid() : f.Workspace,
            changed == "conversation" ? "other" : "chat", "yes", "Sí", state));
        Assert.NotNull(state.Focus!.Suspended); Assert.Equal("RESERVATION", state.CurrentGoal);
    }
}
