using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.Notifications;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

// Real interpreter validation, handler, flows and Firestore SDK fake. Only the
// router's JSON is controlled: these tests do not certify Gemini/Groq language quality.
public sealed class WhatsAppConversationalRepairTests(ITestOutputHelper output)
{
    private static AssistantConversationTests.Fixture Create()
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true)
            { Now = new(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc) };
        f.Products.RemoveAt(2);
        f.Products[0].Name = "Hilo dental"; f.Products[0].PriceMinorUnits = 1290;
        f.Products[0].Metadata["aliases"] = new[] { "hilos", "hilos dentales" };
        f.Products[1].Name = "Enjuague bucal";
        f.Services[1].Name = "Aplicación de flúor";
        f.UseCatalogKnowledge();
        return f;
    }

    private static AiInterpretation Semantic(string text, DialogueAct act, OrderTurnKind? action = null,
        ConversationDirective directive = ConversationDirective.None) => new() {
        Intent = ConversationIntent.Order, Act = act, OrderAction = action, Directive = directive,
        Evidence = [new("Act", text, act.ToString())]
    };

    private async Task Speak(AssistantConversationTests.Fixture f, string text, string source, AiInterpretation? response = null)
    {
        var router = new Mock<IAiRouter>(MockBehavior.Strict);
        var json = JsonSerializer.Serialize(response ?? new() { Intent = ConversationIntent.General },
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), text, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => f.Now);
        var interpreter = new AiInterpreter(router.Object, clock.Object, f.ProfileReads.Object, NullLogger<AiInterpreter>.Instance);
        f.InterpretationOverride = async () => {
            var state = await f.Context();
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
            var turn = DialogueTurnContext.Capture(f.Workspace, source, state,
                TimeZoneInfo.ConvertTime(new DateTimeOffset(f.Now), zone), zone, f.Modules);
            return await interpreter.InterpretAsync(text, turn, default);
        };
        var before = f.AiCalls;
        await f.HandleTurn(text, source);
        var after = await f.Context();
        output.WriteLine($"{source}: {text}\n{f.Sent.GetValueOrDefault(source)?.Content}\nAI={f.AiCalls-before}; goal={after.CurrentGoal}; step={after.CurrentStep}; orders={f.Orders.Count}; reservations={f.Bookings.Count}");
    }

    private async Task<AssistantConversationTests.Fixture> Items()
    {
        var f = Create();
        await Speak(f, "Quiero hacer un pedido", "start");
        await Speak(f, "Quiero 2 hilos dentales", "items");
        Assert.Equal(2, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        return f;
    }

    [Fact]
    public async Task Real_transcript_persists_the_draft_discards_safely_and_never_confirms_a_service_selection()
    {
        var f = Create();
        await Speak(f, "Hola", "hello");
        await Speak(f, "Quieor informacion", "profile", new() { Intent = ConversationIntent.Profile });
        await Speak(f, "Que productos tiene?", "catalog");
        await Speak(f, "Esta disponible el hilo dental?", "stock");
        Assert.DoesNotContain("Enjuague", f.Sent["stock"].Content);
        await Speak(f, "Dije si esta disponible", "reference", new() {
            Intent = ConversationIntent.ProductQuery, Act = DialogueAct.Information,
            UseOfferingReference = true, Question = OfferingQuestion.Stock,
            Evidence = [new("Act", "Dije si esta disponible", nameof(DialogueAct.Information))]
        });
        Assert.Contains("stock", f.Sent["reference"].Content); Assert.DoesNotContain("Enjuague", f.Sent["reference"].Content);
        await Speak(f, "Quiero hacer un pedido", "start");
        await Speak(f, "Quiero 2 hilos dentales", "items");
        Assert.Contains("25.80", f.Sent["items"].Content);
        await Speak(f, "Envía la solicitud", "send", Semantic("Envía la solicitud", DialogueAct.Resume, OrderTurnKind.Submit));
        Assert.StartsWith("ORDER_CONFIRM:", (await f.Context()).CurrentStep); Assert.Empty(f.Orders);
        await Speak(f, "Dije que solo quiero eso", "only", Semantic("Dije que solo quiero eso", DialogueAct.Resume, OrderTurnKind.Submit));
        Assert.Equal(2, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        await Speak(f, "Estúpido dije que solo quiero eso", "frustrated", Semantic("Estúpido dije que solo quiero eso", DialogueAct.Resume, OrderTurnKind.Submit));
        await Speak(f, "Quiero cancelar el pedido", "cancel", Semantic("Quiero cancelar el pedido", DialogueAct.Cancellation, directive: ConversationDirective.CancelCurrentFlow));
        Assert.Equal("CANCEL", (await f.Context()).PendingRecoveryAction);
        await Speak(f, "Sí", "discard"); Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
        await Speak(f, "Quiero hacer una reserva", "booking");
        await Speak(f, "En conepcion", "site", new() { Intent = ConversationIntent.Reservation,
            Act = DialogueAct.RequestedData, Location = "Sede Concepcion",
            Evidence = [new("Act", "En conepcion", nameof(DialogueAct.RequestedData)), new("Location", "conepcion", "Sede Concepcion")] });
        Assert.Equal("sede-a", (await f.Context()).SelectedLocationId);
        await Speak(f, "Limpieza dental?", "service");
        var state = await f.Context();
        Assert.Equal("service-a", state.SelectedServiceId); Assert.Equal("COLLECT_DATE", state.CurrentStep);
        Assert.Null(state.TargetDate); Assert.Null(state.TargetTime); Assert.Null(state.RealCustomerName);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        Assert.DoesNotContain(f.Sent.Values, m => m.Content.Contains("reserva está confirmada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Starting_an_empty_order_after_a_product_query_does_not_spend_the_budget_needed_for_later_location_evidence()
    {
        var f = Create(); await Speak(f, "¿Cuánto cuesta el hilo dental?", "price");
        var state = await f.Context(); Assert.NotNull(OfferingReference.Read(f.Workspace, state));
        Assert.NotNull(ConversationQuestion.Current(f.Workspace, state, f.Now)); var calls = f.AiCalls;
        await Speak(f, "Quiero hacer un pedido", "start");
        Assert.Equal(calls, f.AiCalls); Assert.Equal("ORDER", (await f.Context()).CurrentGoal);
        Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("En Concepción", "Concepción")]
    [InlineData("En conepcion", "conepcion")]
    [InlineData("En sede conepcion", "sede conepcion")]
    [InlineData("En conepción", "conepción")]
    public async Task Location_evidence_with_an_omitted_prefix_or_one_spelling_edit_resolves_only_an_authorized_site(string text, string span)
    {
        // Check the matcher and then the real interpreter/pipeline validator.
        BusinessEntityOption[] allowed = [new("sede-a", "Sede Concepcion"), new("sede-b", "Sede San Jeronimo")];
        Assert.True(new LocationMentionResolver().ResolveEvidence(span, allowed).TryGetValue(out var matched));
        Assert.Equal("sede-a", matched.Id);
        var f = Create(); await Speak(f, "Quiero hacer una reserva", "start");
        await Speak(f, text, "location", new() { Intent = ConversationIntent.Reservation, Act = DialogueAct.RequestedData,
            Location = "Sede Concepcion", Evidence = [new("Act", text, nameof(DialogueAct.RequestedData)), new("Location", span, "Sede Concepcion")] });
        Assert.Equal("sede-a", (await f.Context()).SelectedLocationId);
        Assert.Null((await f.Context()).SelectedServiceId); Assert.Null((await f.Context()).TargetDate); Assert.Null((await f.Context()).TargetTime);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("invented-id")]
    [InlineData("invented-name")]
    [InlineData("outside-workspace")]
    [InlineData("excessive-edit")]
    public async Task Unsubstantiated_or_ambiguous_location_evidence_never_selects_a_site_or_creates_an_operation(string kind)
    {
        var f = Create(); var foreign = Guid.NewGuid();
        if (kind == "ambiguous") f.LocationReads.Setup(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LocationDto("sede-a", "Sede Concepción", "Synthetic", null, null, true),
                new LocationDto("sede-similar", "Sede Conección", "Synthetic", null, null, false)]);
        if (kind == "outside-workspace") {
            f.LocationReads.Setup(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new LocationDto("sede-own", "Sede Lima", "Synthetic", null, null, true)]);
            f.LocationReads.Setup(l => l.GetLocationsAsync(foreign, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new LocationDto("sede-foreign", "Sede Concepcion", "Synthetic", null, null, true)]);
        }
        var span = kind == "invented-name" ? "Atlantida" : kind == "excessive-edit" ? "conpxzzion" : "conepcion";
        var canonical = kind == "invented-id" ? "sede-inventada" : "Sede Concepcion"; var text = "En " + span;
        await Speak(f, "Quiero hacer una reserva", "start");
        await Speak(f, text, "location", new() { Intent = ConversationIntent.Reservation, Act = DialogueAct.RequestedData,
            Location = canonical, Evidence = [new("Act", text, nameof(DialogueAct.RequestedData)), new("Location", span, canonical)] });
        var state = await f.Context(); Assert.Null(state.SelectedLocationId); Assert.Equal("COLLECT_LOCATION", state.CurrentStep);
        Assert.Null(state.SelectedServiceId); Assert.Null(state.TargetDate); Assert.Null(state.TargetTime);
        if (kind == "ambiguous") {
            BusinessEntityOption[] options = [new("sede-a", "Sede Concepción"), new("sede-similar", "Sede Conección")];
            var match = new LocationMentionResolver().ResolveEvidence(span, options);
            Assert.Equal(ResolutionStatus.Ambiguous, match.Status); Assert.False(match.TryGetValue(out _)); Assert.Equal(2, match.Candidates.Count);
            Assert.Equal(ExpectedReply.Selection, state.CurrentQuestion!.Expected);
            Assert.Contains("Sede Concepción", f.Sent["location"].Content); Assert.Contains("Sede Conección", f.Sent["location"].Content);
        }
        f.LocationReads.Verify(l => l.GetLocationsAsync(foreign, It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("Envía la solicitud")]
    [InlineData("Eso es todo")]
    [InlineData("Solo esos dos, nada más")]
    [InlineData("Ya elegí; remítelo para su revisión")]
    public async Task Semantic_finish_creates_only_a_review_then_explicit_consent_has_one_effect(string text)
    {
        var f = await Items();
        await Speak(f, text, "review", Semantic(text, DialogueAct.Resume, OrderTurnKind.Submit));
        Assert.StartsWith("ORDER_CONFIRM:", (await f.Context()).CurrentStep); Assert.Empty(f.Orders);
        await Speak(f, "De acuerdo.", "ack"); Assert.Empty(f.Orders);
        await Speak(f, "Confirmar", "consent"); await f.HandleTurn("Confirmar", "consent");
        var order = Assert.Single(f.Orders.Values);
        Assert.Equal(OrderStatus.PendingReview, order.Status); Assert.Equal(2580, order.TotalAmountMinorUnits);
        Assert.Equal(2, Assert.Single(order.Items).Quantity);
        Assert.Single(f.OrderWrites.Invocations, i => i.Method.Name == "CreateOrderAsync");
    }

    [Theory]
    [InlineData("No conserves los productos quiero eliminarlos")]
    [InlineData("Retira todo lo que apuntamos; prefiero dejarlo")]
    [InlineData("Cancela mi pedido")]
    public async Task Semantic_discard_requires_a_separate_answer_and_cannot_cancel_a_registered_order(string text)
    {
        var f = await Items(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await Speak(f, text, "discard-request", Semantic(text, DialogueAct.Cancellation, directive: ConversationDirective.CancelCurrentFlow));
        Assert.Equal("CANCEL", (await f.Context()).PendingRecoveryAction);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        await Speak(f, "Sí", "discard-consent"); Assert.Null((await f.Context()).CurrentGoal);
        Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_semantic_absolute_quantity_replaces_instead_of_adding_and_revokes_the_review()
    {
        var f = await Items(); await Speak(f, "finalizar", "review");
        const string text = "Solo quiero 2 hilos";
        var semantic = Semantic(text, DialogueAct.Correction, OrderTurnKind.Edit);
        semantic.Items = [new("Hilo dental", "2 hilos", OrderEditKind.Set, 2, "2")];
        await Speak(f, text, "set", semantic);
        Assert.Equal(2, Assert.Single((await f.Context()).OrderDraftItems).Quantity); Assert.Empty(f.Orders);
        await Speak(f, "Confirmar", "new-review"); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Stock_information_interrupts_order_without_changing_it_or_creating_effects()
    {
        var f = await Items(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await Speak(f, "¿Está disponible el hilo dental?", "stock");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Contains("stock", f.Sent["stock"].Content); Assert.DoesNotContain("Enjuague", f.Sent["stock"].Content);
        Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Missing_provider_keeps_draft_and_offers_a_useful_next_action()
    {
        var f = await Items(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.InterpretationOverride = () => Task.FromResult(new AiInterpretation { Intent = ConversationIntent.ProviderUnavailable });
        await f.HandleTurn("Todavía no lo tengo claro", "outage");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Contains("«", f.Sent["outage"].Content); Assert.Contains("asesor", f.Sent["outage"].Content);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Clearing_items_preserves_the_order_goal_and_requires_its_own_confirmation()
    {
        var f = await Items(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        const string text = "Vacía los artículos; luego escogeré otros";
        await Speak(f, text, "clear", Semantic(text, DialogueAct.Correction, OrderTurnKind.Clear));
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal("CLEAR_ORDER_ITEMS", (await f.Context()).PendingRecoveryAction);
        await Speak(f, "Sí", "clear-consent");
        Assert.Equal("ORDER", (await f.Context()).CurrentGoal); Assert.Empty((await f.Context()).OrderDraftItems);
        await Speak(f, "Confirmar", "cannot-send-empty"); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("conversation")]
    [InlineData("expired")]
    public async Task An_unrelated_or_expired_question_cannot_authorize_clearing_the_draft(string kind)
    {
        var f = await Items(); const string text = "Vacía los artículos del borrador";
        await Speak(f, text, "clear", Semantic(text, DialogueAct.Correction, OrderTurnKind.Clear));
        var state = await f.Context(); var question = state.CurrentQuestion!;
        state.CurrentQuestion = kind switch {
            "workspace" => question with { WorkspaceId = Guid.NewGuid() },
            "conversation" => question with { ConversationId = "unrelated-conversation" },
            _ => question with { ExpiresAt = f.Now.AddSeconds(-1) }
        };
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await Speak(f, "Sí", "unrelated-answer");
        Assert.Equal(2, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        Assert.Equal("CLEAR_ORDER_ITEMS", (await f.Context()).PendingRecoveryAction); Assert.Empty(f.Orders);
        await Speak(f, "Sí", "renewed-answer"); Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Delayed_booking_result_is_not_resumed_for_an_unrelated_current_message()
    {
        var f = Create();
        await Speak(f, "Soy Elena Flores, quiero reservar aplicación de flúor mañana a las 10:00 en Concepción", "old-draft");
        await Speak(f, "Confirmar", "old-consent"); Assert.Single(f.Bookings);
        var previous = f.Sent["old-consent"].Content;
        await Speak(f, "Quiero reservar", "new-start"); await Speak(f, "Concepción", "new-site");
        await Speak(f, "Limpieza dental?", "new-service");
        Assert.Single(f.Bookings); Assert.Equal("service-a", (await f.Context()).SelectedServiceId);
        Assert.DoesNotContain(previous, f.Sent["new-service"].Content);
        Assert.Equal("COLLECT_DATE", (await f.Context()).CurrentStep);
        await f.HandleTurn("Confirmar", "old-consent");
        Assert.Equal(previous, f.Sent["old-consent"].Content); Assert.Single(f.Bookings);
        Assert.Equal("service-a", (await f.Context()).SelectedServiceId);
    }

    [Fact]
    public async Task The_real_semantic_prompt_has_only_a_current_authorized_offering_reference()
    {
        var f = Create(); await Speak(f, "¿Está disponible el hilo dental?", "product");
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => f.Now);
        var resolver = new WorkspaceSlotResolver(f.EntitlementReads.Object, f.ProfileReads.Object, f.LocationReads.Object,
            f.OfferingReads.Object, clock.Object, new(), new(), new(), new(), new(), NullLogger<WorkspaceSlotResolver>.Instance);
        string? prompt = null;
        var router = new Mock<IAiRouter>();
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback((AiTaskType _, string p, string _, bool _, CancellationToken _) => prompt = p)
            .ReturnsAsync("{\"Intent\":\"General\"}");
        var interpreter = new AiInterpreter(router.Object, clock.Object, f.ProfileReads.Object, NullLogger<AiInterpreter>.Instance);
        var now = f.Now;
        foreach (var variant in new[] { "current", "expired", "foreign", "revoked", "new-question" })
        {
            f.Now = now; f.Modules.Add("CATALOG");
            var state = await f.Context();
            if (variant == "expired") f.Now = f.Now.AddMinutes(11);
            if (variant == "foreign") new OfferingReference(Guid.NewGuid(), "PRODUCT", ["product-a"], null).Remember(state);
            if (variant == "revoked") f.Modules.Remove("CATALOG");
            if (variant == "new-question") ConversationQuestion.Remember(f.Workspace, "different-question", state,
                ExpectedReply.Information, ConversationIntent.Location, "¿Qué sede necesitas?", f.Now);
            var turn = await resolver.ResolveAsync(f.Workspace, variant, "Dije si está disponible", state,
                SlotResolutionPurpose.Information, new(false, false, false), default);
            await interpreter.InterpretAsync("Dije si está disponible", turn.Context, default);
            if (variant == "current") { Assert.Contains("ReferencedOffering", prompt); Assert.Contains("Hilo dental", prompt); }
            else Assert.DoesNotContain("Hilo dental", prompt);
            Assert.DoesNotContain("12.90", prompt); Assert.DoesNotContain(AssistantConversationTests.Fixture.Phone, prompt);
        }
    }

    [Theory]
    [InlineData("03/11", "2026-11-03")]
    [InlineData("3 de noviembre", "2026-11-03")]
    [InlineData("mañana por la tarde", "2026-10-31")]
    public async Task Guided_dates_use_business_time_without_selecting_an_implicit_hour_or_committing(string text, string expectedDate)
    {
        var f = Create(); await Speak(f, "Quiero reservar limpieza dental en Concepción", "booking");
        Assert.Contains("«03/11»", f.Sent["booking"].Content);
        await Speak(f, text, "date"); var state = await f.Context();
        Assert.Equal(expectedDate, state.TargetDate); Assert.Null(state.TargetTime); Assert.Empty(f.Bookings);
        await Speak(f, "De acuerdo.", "not-consent"); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("license")]
    [InlineData("location")]
    public async Task A_quote_change_or_revoked_license_never_reuses_old_consent(string change)
    {
        var f = await Items();
        var state = await f.Context(); state.SelectedLocationId = "sede-a";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await Speak(f, "finalizar", "review"); Assert.Empty(f.Orders);
        if (change == "price") f.Products[0].PriceMinorUnits++;
        if (change == "license") f.Modules.Remove("ORDERS");
        if (change == "location") { f.Products[0].LocationScope = "SPECIFIC"; f.Products[0].LocationIds = ["sede-b"]; }
        await Speak(f, "Confirmar", "old-consent"); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task A_malformed_or_ambiguous_provider_response_cannot_discard_or_edit_the_draft()
    {
        var f = await Items(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        var malformed = Semantic("texto que el cliente no ha escrito", DialogueAct.Cancellation, directive: ConversationDirective.CancelCurrentFlow);
        await Speak(f, "Explícame la selección", "forged", malformed);
        Assert.Null((await f.Context()).PendingRecoveryAction);
        var ambiguous = Semantic("No estoy segura de quitarlo", DialogueAct.Cancellation, directive: ConversationDirective.CancelCurrentFlow);
        ambiguous.RequiresClarification = true;
        await Speak(f, "No estoy segura de quitarlo", "ambiguous", ambiguous);
        Assert.Null((await f.Context()).PendingRecoveryAction);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Clear_and_discard_proposals_are_rejected_after_a_human_takes_control_during_interpretation()
    {
        var f = await Items(); var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.DuringInterpretation = async () => {
            var state = await f.Context(); state.Mode = "Human";
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        };
        const string text = "Vacía la selección y la reharé";
        await Assert.ThrowsAsync<NexFlow.Domain.Exceptions.ConcurrencyException>(() =>
            Speak(f, text, "takeover", Semantic(text, DialogueAct.Correction, OrderTurnKind.Clear)));
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings); Assert.DoesNotContain("takeover", f.Sent.Keys);
    }

    [Fact]
    public async Task Durable_late_outbound_keeps_its_original_source_workspace_and_response()
    {
        var store = new InMemoryFirestore(); var workspace = Guid.NewGuid();
        AssistantRc1AdversarialTests.SeedConversation(store, workspace);
        var repo = new FirestoreConversationRepository(store.Db);
        var gateway = new Mock<IMessageGateway>(MockBehavior.Strict);
        var deliveries = new List<(Guid Workspace, string Phone, string Text, string Key)>();
        gateway.Setup(g => g.SendTextAsync(workspace, "phone", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid w, string phone, string text, string key, Func<CancellationToken, Task> start, CancellationToken ct) => {
                await start(ct); deliveries.Add((w, phone, text, key)); return "provider-" + deliveries.Count;
            });
        var service = new OutboundMessageService(repo, gateway.Object, Mock.Of<INotificationService>(), NullLogger<OutboundMessageService>.Instance);
        var key = $"{workspace:N}:AI:old-consent:response:1";
        var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
        await repo.PrepareOutboundAsync(workspace, "chat", "phone", new() {
            Id = id, IdempotencyKey = key, Sender = SenderType.AI, Direction = "outbound", Content = "Confirmación de una operación anterior",
            Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow.AddMinutes(-10)
        }, default);
        var current = await service.SendMessageAsync(workspace, "chat", "phone", "¿Para qué fecha deseas reservar?", SenderType.AI, "new-service", default);
        Assert.False(await service.TryResumeResponseAsync(Guid.NewGuid(), "old-consent", default));
        Assert.False(await service.TryResumeResponseAsync(workspace, "unrelated", default));
        Assert.True(await service.TryResumeResponseAsync(workspace, "old-consent", default));
        Assert.True(await service.TryResumeResponseAsync(workspace, "old-consent", default));
        Assert.Equal(2, deliveries.Count);
        Assert.Equal(key, deliveries[1].Key); Assert.Equal(workspace, deliveries[1].Workspace); Assert.Equal("phone", deliveries[1].Phone);
        var persisted = (await repo.GetOutboundAsync(workspace, current.Id, default))!.Value.Message;
        Assert.Equal("¿Para qué fecha deseas reservar?", persisted.Content);
        Assert.DoesNotContain("old-consent", persisted.IdempotencyKey);
    }

    [Theory]
    [InlineData("¿Hay hilo dental?")]
    [InlineData("¿Les queda ese producto?")]
    public async Task A_specific_product_fact_never_implies_verified_physical_stock(string text)
    {
        var f = Create(); await Speak(f, "¿Cuánto cuesta Hilo dental?", "reference");
        await Speak(f, text, "stock", new() {
            Intent = ConversationIntent.ProductQuery, Act = DialogueAct.Information, Question = OfferingQuestion.Stock,
            UseOfferingReference = text.Contains("ese"),
            Evidence = [new("Act", text, nameof(DialogueAct.Information))]
        });
        Assert.Contains("stock", f.Sent["stock"].Content); Assert.DoesNotContain("Enjuague", f.Sent["stock"].Content);
        Assert.Empty((await f.Context()).OrderDraftItems); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }
}
