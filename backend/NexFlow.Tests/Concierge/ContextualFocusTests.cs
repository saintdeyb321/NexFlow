using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class ContextualFocusTests(ITestOutputHelper output)
{
    private static AssistantConversationTests.Fixture Create() => new(conciergeCatalog: true, intelligentCommerce: true, contextualFocus: true)
        { Now = new(2026, 10, 31, 14, 0, 0, DateTimeKind.Utc) };

    private async Task<AssistantConversationTests.Fixture> Order()
    {
        var f = Create();
        await f.HandleTurn("Quiero hacer un pedido", "order-start");
        await f.HandleTurn("dos panes integrales y un queque", "order-items");
        Assert.Equal(new[] { 2, 1 }, (await f.Context()).OrderDraftItems.Select(i => i.Quantity));
        return f;
    }

    private async Task Speak(AssistantConversationTests.Fixture f, string text, string source,
        NexFlow.Domain.Enums.ConversationIntent intent, DialogueAct act)
    {
        f.InterpretationOverride = () => Task.FromResult(new AiInterpretation {
            Intent = intent, Act = act, Evidence = [new("Act", text, act.ToString())]
        });
        var before = f.AiCalls;
        await f.HandleTurn(text, source);
        output.WriteLine($"{source}: AI={f.AiCalls-before}; {f.Sent.GetValueOrDefault(source)?.Content}");
        output.WriteLine(JsonSerializer.Serialize(new { State = AssistantConversationTests.Fixture.BusinessDraft(await f.Context()), f.AiCalls,
            Orders = f.Orders.Count, Reservations = f.Bookings.Count }));
    }

    [Fact]
    public async Task An_open_order_can_pause_for_a_reservation_and_return_without_business_effects()
    {
        var f = await Order();
        await Speak(f, "También quiero hacer una reserva", "switch", NexFlow.Domain.Enums.ConversationIntent.Reservation, DialogueAct.StartOperation);
        Assert.Equal("RESERVATION", (await f.Context()).CurrentGoal);
        await f.HandleTurn("En Concepción, limpieza dental", "booking-details");
        Assert.Equal("service-a", (await f.Context()).SelectedServiceId);
        await Speak(f, "Mejor volvamos al pedido", "resume", NexFlow.Domain.Enums.ConversationIntent.Order, DialogueAct.Resume);
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal);
        Assert.Equal(new[] { ("product-a", 2), ("product-b", 1) }, (await f.Context()).OrderDraftItems.Select(i => (i.ProductId, i.Quantity)));
        Assert.Null((await f.Context()).SelectedServiceId);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    private async Task Switch(AssistantConversationTests.Fixture f, string source = "switch") =>
        await Speak(f, "También quiero hacer una reserva", source, ConversationIntent.Reservation, DialogueAct.StartOperation);
    private async Task ResumeOrder(AssistantConversationTests.Fixture f, string source = "resume") =>
        await Speak(f, "Mejor volvamos al pedido", source, ConversationIntent.Order, DialogueAct.Resume);

    [Fact]
    public async Task Independent_drafts_need_new_reviews_and_only_one_order_is_committed()
    {
        var watch = Stopwatch.StartNew(); var f = await Order();
        await f.HandleTurn("finalizar", "old-order-review"); var oldQuestion = (await f.Context()).CurrentQuestion!;
        await Switch(f);
        await f.HandleTurn("Soy Elena Flores, quiero reservar limpieza dental mañana a las 10:00 en Concepción", "booking-data");
        var booking = await f.Context(); Assert.Equal("CONFIRM", booking.CurrentStep);
        var bookingId = booking.Focus!.Active.Id;
        Assert.Equal(bookingId, booking.CurrentQuestion!.OperationId); Assert.Empty(f.Bookings);
        await ResumeOrder(f); var order = await f.Context();
        var bookingSnapshot = JsonSerializer.Serialize(order.Focus!.Suspended);
        Assert.Equal(bookingId, order.Focus.Suspended!.Identity.Id);
        Assert.Null(order.RealCustomerName); Assert.Null(order.TargetDate); Assert.Null(order.SelectedServiceId);
        Assert.NotEqual(oldQuestion.QuestionId, order.CurrentQuestion!.QuestionId);
        await f.HandleTurn("Confirmar", "late-order-consent"); Assert.Empty(f.Orders);
        await f.HandleTurn("cambia Pan integral a uno", "edit-order");
        Assert.Equal(1, (await f.Context()).OrderDraftItems.Single(i => i.ProductId == "product-a").Quantity);
        Assert.Equal(bookingSnapshot, JsonSerializer.Serialize((await f.Context()).Focus!.Suspended));
        await f.HandleTurn("finalizar", "new-order-review");
        var review = (await f.Context()).CurrentQuestion!;
        Assert.Equal((await f.Context()).Focus!.Active.Id, review.OperationId);
        await f.HandleTurn("De acuerdo.", "h06-ack"); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
        await f.HandleTurn("Confirmar", "order-consent"); await f.HandleTurn("Confirmar", "order-consent");
        Assert.Equal(OrderStatus.PendingReview, Assert.Single(f.Orders.Values).Status);
        Assert.Single(f.OrderWrites.Invocations, i => i.Method.Name == "CreateOrderAsync"); Assert.Empty(f.Bookings);
        Assert.Equal(bookingSnapshot, JsonSerializer.Serialize((await f.Context()).Focus!.Suspended));
        await Speak(f, "Retomemos la cita que dejamos preparada", "resume-booking", ConversationIntent.Reservation, DialogueAct.Resume);
        Assert.Equal(bookingId, (await f.Context()).Focus!.Active.Id);
        Assert.Equal("Elena Flores", (await f.Context()).RealCustomerName);
        await f.HandleTurn("Sí", "old-booking-consent"); Assert.Empty(f.Bookings);
        Assert.Equal(ExpectedReply.ConfirmReservation, (await f.Context()).CurrentQuestion!.Expected);
        await f.HandleTurn("De acuerdo.", "booking-ack"); Assert.Empty(f.Bookings);
        await f.HandleTurn("Confirmar", "fresh-booking-consent"); Assert.Single(f.Bookings);
        Assert.Null((await f.Context()).Focus); // Neither completed identity can authorize a later preparation.
        await f.HandleTurn("Quiero hacer un pedido", "unrelated-order");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal); Assert.Empty((await f.Context()).OrderDraftItems);
        Assert.Single(f.Orders); Assert.Single(f.Bookings);
        output.WriteLine($"Vertical: AI={f.AiCalls}, elapsed_fixture_ms={watch.ElapsedMilliseconds}, orders={f.Orders.Count}, reservations={f.Bookings.Count}");
    }

    [Fact]
    public async Task A_fresh_recovery_service_can_load_the_suspended_draft_after_restart()
    {
        var f = await Order(); await Switch(f);
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => f.Now);
        var restarted = new ContextRecoveryService(Mock.Of<IConversationCache>(), new FirestoreConversationStateRepository(f.Firestore.Db), clock.Object);
        var state = await restarted.GetOrRecoverContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, default);
        Assert.Equal(2, state.Focus!.Suspended!.Items.Count);
        var service = new ConversationFocusService(restarted, clock.Object, Options.Create(new AssistantRecoveryOptions { EnableContextualFocus = true }));
        Assert.Null(await service.ApplyAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, "chat", "restart-resume", state,
            new(f.Workspace, "restart-resume", state.StateVersion, OperationKind.Order, true), f.Modules, default));
        var recovered = await f.Context();
        Assert.Equal(new[] { ("product-a", 2), ("product-b", 1) }, recovered.OrderDraftItems.Select(i => (i.ProductId, i.Quantity)));
        Assert.Equal("ORDER", recovered.CurrentGoal); Assert.Null(recovered.CurrentQuestion);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Suspended_ttl_is_not_extended_by_work_on_the_other_focus()
    {
        var f = await Order(); await Switch(f); var paused = (await f.Context()).Focus!.Suspended!;
        f.Now = f.Now.AddHours(25);
        var active = await f.Context(); await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, active, default);
        await ResumeOrder(f);
        var state = await f.Context(); Assert.Equal("RESERVATION", state.CurrentGoal);
        Assert.Equal(paused.Identity, state.Focus!.Suspended!.Identity); Assert.Empty(state.OrderDraftItems);
        Assert.Contains("vencido", f.Sent["resume"].Content); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("product")]
    [InlineData("license")]
    public async Task Resume_revalidates_catalog_and_license_instead_of_reusing_old_consent(string change)
    {
        var f = await Order(); await f.HandleTurn("finalizar", "old-review"); await Switch(f);
        if (change == "price") f.Products[0].PriceMinorUnits = 350;
        if (change == "product") f.Products[0].IsActive = false;
        if (change == "license") f.Modules.Remove("ORDERS");
        await ResumeOrder(f);
        if (change == "license") Assert.Equal("RESERVATION", (await f.Context()).CurrentGoal);
        else {
            Assert.Equal("ORDER", (await f.Context()).CurrentGoal);
            if (change == "price") Assert.Contains("17.00", f.Sent["resume"].Content);
            await f.HandleTurn("Confirmar", "stale-consent"); Assert.Empty(f.Orders);
        }
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Competing_focus_writers_have_one_cas_winner_and_preserve_the_order()
    {
        var f = await Order(); var a = await f.Context(); var b = await f.Context();
        f.Firestore.SynchronizeReads($"workspaces/{f.Workspace}/conversationStates/{AssistantConversationTests.Fixture.Phone}");
        async Task<bool> Apply(ConversationContextDto state, string source)
        {
            try { return await f.Focus.ApplyAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, "chat", source, state,
                new(f.Workspace, source, state.StateVersion, OperationKind.Reservation, false), f.Modules, default) == null; }
            catch (ConcurrencyException) { return false; }
        }
        var result = await Task.WhenAll(Apply(a, "race-a"), Apply(b, "race-b"));
        Assert.Single(result, r => r); Assert.True(f.Firestore.AbortedCommits >= 1);
        var durable = await f.Context(); Assert.Equal("RESERVATION", durable.CurrentGoal);
        Assert.Equal(new[] { 2, 1 }, durable.Focus!.Suspended!.Items.Select(i => i.Quantity));
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_source_or_unknown_delivery_does_not_repeat_transition_or_interpretation(bool uncertain)
    {
        var f = await Order(); if (uncertain) f.DeliveryStatus = MessageStatus.UnknownDelivery;
        await Switch(f); var state = JsonSerializer.Serialize(await f.Context()); var calls = f.AiCalls;
        await f.HandleTurn("También quiero hacer una reserva", "switch");
        Assert.Equal(state, JsonSerializer.Serialize(await f.Context())); Assert.Equal(calls, f.AiCalls);
        Assert.Equal(uncertain ? MessageStatus.UnknownDelivery : MessageStatus.Sent, f.Sent["switch"].Status);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Another_workspace_cannot_resume_a_foreign_envelope()
    {
        var a = await Order(); await Switch(a); var b = await Order(); var state = await b.Context();
        state.Focus = JsonSerializer.Deserialize<ConversationFocusState>(JsonSerializer.Serialize((await a.Context()).Focus));
        await b.Contexts.SaveContextAsync(b.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await Assert.ThrowsAsync<ConcurrencyException>(() => Switch(b));
        Assert.Equal("ORDER", (await b.Context()).CurrentGoal); Assert.Empty(b.Orders); Assert.Empty(b.Bookings);
        Assert.DoesNotContain("switch", b.Sent.Keys);
    }

    [Fact]
    public async Task Takeover_during_interpretation_invalidates_the_switch()
    {
        var f = await Order(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.DuringInterpretation = async () => { var state = await f.Context(); state.Mode = "Human";
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default); };
        await Assert.ThrowsAsync<ConcurrencyException>(() => Switch(f));
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Null((await f.Context()).Focus);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("ambiguous")]
    [InlineData("evidence")]
    public async Task Insufficient_semantics_cannot_switch_or_discard_the_order(string kind)
    {
        var f = await Order(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.InterpretationOverride = () => Task.FromResult(kind == "provider" ? new AiInterpretation { Intent = ConversationIntent.ProviderUnavailable }
            : new AiInterpretation { Intent = ConversationIntent.Reservation, RequiresClarification = kind == "ambiguous" });
        await f.HandleTurn("Quiero reservar", "uncertain");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Null((await f.Context()).Focus);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("recovery")]
    [InlineData("order-commit")]
    [InlineData("reservation-commit")]
    public async Task Pending_decisions_or_commits_cannot_be_suspended(string pending)
    {
        var f = await Order(); var state = await f.Context();
        if (pending == "recovery") state.PendingRecoveryAction = "CANCEL";
        if (pending == "order-commit") state.CurrentStep = "ORDER_COMMIT:pending";
        if (pending == "reservation-commit") state.ReservationCommitSourceMessageId = "pending";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        var proposal = new FocusChangeProposal(f.Workspace, "blocked", state.StateVersion, OperationKind.Reservation, false);
        Assert.NotNull(await f.Focus.ApplyAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, "chat", "blocked", state, proposal, f.Modules, default));
        Assert.Null((await f.Context()).Focus); Assert.Equal(new[] { 2, 1 }, (await f.Context()).OrderDraftItems.Select(i => i.Quantity));
    }

    [Fact]
    public async Task Legacy_json_and_default_disabled_flag_keep_the_previous_semantics_and_hash()
    {
        Assert.False(new AssistantRecoveryOptions().EnableContextualFocus);
        var legacy = "{\"StateVersion\":\"old\",\"CurrentGoal\":\"ORDER\",\"CurrentStep\":\"ORDER_COLLECT_ITEMS\",\"OrderDraftItems\":[{\"ProductId\":\"p\",\"ProductName\":\"Producto\",\"Quantity\":2}]}";
        var state = JsonSerializer.Deserialize<ConversationContextDto>(legacy)!;
        Assert.Null(state.Focus); Assert.Equal(2, Assert.Single(state.OrderDraftItems).Quantity);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            state.CurrentGoal, state.CurrentStep, state.SelectedLocationId, state.SelectedServiceId,
            state.TargetDate, state.TargetTime, state.PreferredTime, state.RealCustomerName,
            state.PendingCustomerName, state.PendingServiceIds, state.PendingDialogueSlots,
            state.OrderDraftItems, state.PendingRecoveryAction
        }))));
        Assert.Equal(hash, ConversationQuestion.Fingerprint(state));
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true);
        f.Firestore.Seed($"workspaces/{f.Workspace}/conversationStates/{AssistantConversationTests.Fixture.Phone}",
            new Dictionary<string, Google.Cloud.Firestore.V1.Value> {
                ["contextJson"] = new() { StringValue = legacy.Replace("\"p\"", "\"product-a\"").Replace("\"Producto\"", "\"Pan integral\"") }
            });
        var legacyDurable = await f.Context(); Assert.Null(legacyDurable.Focus); Assert.Equal(2, Assert.Single(legacyDurable.OrderDraftItems).Quantity);
        await f.HandleTurn("ver pedido", "legacy-read"); Assert.Null((await f.Context()).Focus); Assert.Contains("Pan integral", f.Sent["legacy-read"].Content);
        await f.HandleTurn("Quiero hacer un pedido", "start"); await Switch(f);
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal); Assert.Null((await f.Context()).Focus);
    }

    [Fact]
    public async Task Read_only_interruptions_keep_both_drafts_and_consult_real_fixture_availability()
    {
        var f = await Order(); await Switch(f);
        await f.HandleTurn("Soy Elena Flores, quiero reservar limpieza dental mañana a las 10:00 en Concepción", "booking-data");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        var paused = JsonSerializer.Serialize((await f.Context()).Focus!.Suspended);
        foreach (var (text, source) in new[] { ("¿Dónde están?", "locations"), ("¿Qué servicios ofrecen?", "services"), ("¿Está libre mañana a las 10:00?", "availability") })
        {
            await f.HandleTurn(text, source);
            Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
            Assert.Equal(paused, JsonSerializer.Serialize((await f.Context()).Focus!.Suspended));
        }
        Assert.Contains(f.EngineReads.Invocations, i => i.Method.Name == "GetAvailabilityAtAsync");
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_pre_switch_question_cannot_be_restored_as_consent_for_the_resumed_operation()
    {
        var f = await Order(); await Switch(f);
        await f.HandleTurn("Soy Elena Flores, quiero reservar limpieza dental mañana a las 10:00 en Concepción", "booking-data");
        var old = (await f.Context()).CurrentQuestion!;
        await ResumeOrder(f);
        await Speak(f, "Retomemos la reserva anterior", "back", ConversationIntent.Reservation, DialogueAct.Resume);
        var resumed = await f.Context(); Assert.Equal(old.OperationId, resumed.Focus!.Active.Id);
        Assert.NotEqual(old.ActivationRevision, resumed.Focus.Active.ActivationRevision);
        resumed.CurrentQuestion = old;
        Assert.Null(ConversationQuestion.Current(f.Workspace, resumed, f.Now));
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, resumed, default);
        await f.HandleTurn("Sí", "old-answer"); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task A_failure_after_the_atomic_focus_save_retries_without_another_semantic_call_or_losing_drafts()
    {
        var f = await Order(); await Switch(f);
        f.OfferingReads.Setup(o => o.GetProductsAsync(f.Workspace, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, string? _, string? _, CancellationToken _) => {
                if ((await f.Context()).CurrentGoal == "ORDER") throw new TimeoutException("Synthetic catalog outage after focus save");
                return f.Products.ToArray();
            });
        await Assert.ThrowsAsync<TimeoutException>(() => ResumeOrder(f));
        var state = await f.Context(); Assert.Equal("ORDER", state.CurrentGoal); Assert.NotNull(state.Focus!.Suspended);
        var identity = state.Focus.Active; var calls = f.AiCalls;
        f.OfferingReads.Setup(o => o.GetProductsAsync(f.Workspace, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(f.Products.ToArray());
        await f.HandleTurn("Mejor volvamos al pedido", "resume");
        Assert.Equal(calls, f.AiCalls); Assert.Equal(identity, (await f.Context()).Focus!.Active);
        Assert.Equal(new[] { 2, 1 }, (await f.Context()).OrderDraftItems.Select(i => i.Quantity));
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Prompt_projection_adds_only_the_authorized_kind_and_measures_characters_without_history_or_ids()
    {
        var f = await Order(); await Switch(f);
        var state = await f.Context(); var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        var context = DialogueTurnContext.Capture(f.Workspace, "projection", state, TimeZoneInfo.ConvertTime(new DateTimeOffset(f.Now), zone), zone, f.Modules);
        var prompts = new List<string>(); var router = new Mock<IAiRouter>();
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback((AiTaskType _, string p, string _, bool _, CancellationToken _) => prompts.Add(p)).ReturnsAsync("{\"Intent\":\"General\"}");
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => f.Now);
        var interpreter = new AiInterpreter(router.Object, clock.Object, f.ProfileReads.Object, NullLogger<AiInterpreter>.Instance);
        await interpreter.InterpretAsync("Retomemos lo anterior", context, default);
        await interpreter.InterpretAsync("Retomemos lo anterior", context with { SupportsFocusSwitch = true }, default);
        Assert.Equal(2, prompts.Count); Assert.Contains("Borrador suspendido: Order", prompts[1]);
        Assert.DoesNotContain(state.Focus!.Active.Id.ToString(), prompts[1]);
        Assert.DoesNotContain(state.Focus.Suspended!.Identity.Id.ToString(), prompts[1]);
        Assert.DoesNotContain(AssistantConversationTests.Fixture.Phone, prompts[1]);
        Assert.DoesNotContain("Pan integral", prompts[1]); Assert.DoesNotContain("Queque", prompts[1]);
        Assert.InRange(prompts[1].Length - prompts[0].Length, 1, 600);
        output.WriteLine($"Prompt characters: disabled={prompts[0].Length}, enabled={prompts[1].Length}, delta={prompts[1].Length-prompts[0].Length}; provider tokens/cost: not measured");
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"Intent\":\"Order\",\"FocusProposal\":{\"Target\":\"Order\"}}");
        var forged = await interpreter.InterpretAsync("Retomemos el pedido", context with { SupportsFocusSwitch = true }, default);
        Assert.Equal(ConversationIntent.ProviderUnavailable, forged.Intent); Assert.Null(forged.FocusProposal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_quantity_reference_after_resume_is_accepted_only_for_one_freshly_verified_item(bool ambiguous)
    {
        var f = Create(); await f.HandleTurn("Quiero hacer un pedido", "start");
        await f.HandleTurn(ambiguous ? "dos panes integrales y un queque" : "dos panes integrales", "items");
        await Switch(f); await ResumeOrder(f);
        const string text = "Deja solo uno";
        f.InterpretationOverride = () => Task.FromResult(new AiInterpretation {
            Intent = ConversationIntent.Order, Act = DialogueAct.Correction, OrderAction = OrderTurnKind.Edit,
            UseOfferingReference = true, Items = [new("Pan integral", "uno", OrderEditKind.Set, 1, "uno")],
            Evidence = [new("Act", text, nameof(DialogueAct.Correction))]
        });
        await f.HandleTurn(text, "quantity");
        Assert.Equal(ambiguous ? 2 : 1, (await f.Context()).OrderDraftItems.Single(i => i.ProductId == "product-a").Quantity);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }
}
