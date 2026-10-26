using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class RuntimeEfficiencyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("clinic", true, false)]
    [InlineData("commerce", false, true)]
    [InlineData("mixed", true, true)]
    public async Task Mvp_configured_businesses_share_the_runtime_and_keep_read_interruptions_and_failure_safe(string business, bool reservations, bool orders)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true) {
            Now = new(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc) };
        if (!reservations) { f.Modules.Remove("SERVICES"); f.Modules.Remove("RESERVATIONS"); }
        if (!orders) { f.Modules.Remove("CATALOG"); f.Modules.Remove("ORDERS"); }
        RuntimeReadToolsTests.UseRealKnowledge(f);
        f.ProfileReads.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessProfileDto("Negocio sintético " + business, "PRIVATE-TAX", "", "", "Actividad configurada " + business, "America/Lima"));
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Profile });
        await f.HandleTurn("¿Qué actividad desarrolla este negocio?", "profile");
        Assert.Contains("Actividad configurada " + business, f.Sent["profile"].Content);
        Assert.DoesNotContain("PRIVATE-TAX", f.Sent["profile"].Content);
        if (reservations)
        {
            await f.HandleTurn("muéstrame los servicios", "services");
            Assert.Contains(f.Services[0].Name, f.Sent["services"].Content);
            await f.HandleTurn("¿Cuánto cuesta limpieza dental?", "service-price");
            Assert.Contains((f.Services[0].PriceMinorUnits / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), f.Sent["service-price"].Content);
            await f.HandleTurn("Soy Ana Pérez, quiero limpieza dental en Concepción pasado mañana a las 10", "prepare-reservation");
            var incomplete = await f.Context();
            Assert.Equal("2026-10-28", incomplete.TargetDate); Assert.Null(incomplete.TargetTime);
            Assert.Equal("COLLECT_TIME", incomplete.CurrentStep); Assert.Empty(f.Bookings);
            // A bare hour in compound prose is legitimately ambiguous. Supply
            // the requested precision rather than inventing an hour or consent.
            await f.HandleTurn("10:00", "precise-time");
            Assert.Equal(ExpectedReply.ConfirmReservation, (await f.Context()).CurrentQuestion?.Expected);
            Assert.Empty(f.Bookings);
            var draft = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
            await f.HandleTurn("¿Dónde están?", "location-interruption");
            Assert.Equal(draft, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
            await FailureDoesNotChangeDraft();
            await f.HandleTurn("continuemos", "resume-reservation");
            Assert.Empty(f.Bookings);
            await f.HandleTurn("sí confirmo", "confirm-reservation"); await f.HandleTurn("sí confirmo", "confirm-reservation");
            Assert.Single(f.Bookings); Assert.Empty(f.Orders);
        }
        if (orders)
        {
            const string question = "¿Se puede hacer un pedido?";
            f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order, Act = DialogueAct.OperationInformation,
                Evidence = [new("Act", question, nameof(DialogueAct.OperationInformation))] });
            await f.HandleTurn(question, "order-capability");
            Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Orders);
            await f.HandleTurn("quiero hacer un pedido", "start-order");
            await f.HandleTurn("tres panes integrales y dos queques", "order-items");
            Assert.Equal(2, (await f.Context()).OrderDraftItems.Count); Assert.Empty(f.Orders);
            var draft = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
            await f.HandleTurn("¿Qué horarios tienen?", "hours-interruption");
            Assert.Contains("08:00", f.Sent["hours-interruption"].Content);
            Assert.Equal(draft, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
            await FailureDoesNotChangeDraft();
            await f.HandleTurn("ver pedido", "review-order");
            await f.HandleTurn("envía el pedido", "submit-order"); await f.HandleTurn("envía el pedido", "submit-order");
            Assert.Equal(OrderStatus.PendingReview, Assert.Single(f.Orders.Values).Status);
        }
        Assert.All(f.FactualQueries, q => Assert.Equal(f.Workspace, q.Workspace));
        Assert.All(f.OfferingReads.Invocations, i => Assert.Equal(f.Workspace, i.Arguments[0]));

        async Task FailureDoesNotChangeDraft()
        {
            var draft = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
            var calls = f.AiCalls;
            f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProviderUnavailable });
            await f.HandleTurn("¿Podrías orientarme un poco?", "provider-failure-" + calls);
            Assert.Equal(calls + 1, f.AiCalls);
            Assert.Equal(draft, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
            Assert.Empty(f.Orders);
        }
    }

    [Fact]
    public async Task Knowledge_trace_identifies_the_tool_without_logging_business_data()
    {
        var f = new AssistantConversationTests.Fixture();
        f.ProfileReads.Setup(r => r.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessProfileDto("PRIVATE-BUSINESS-MARKER", "", "", "", "PRIVATE-FACT-MARKER"));
        var log = new Mock<ILogger<KnowledgeService>>();
        var service = new KnowledgeService(f.ProfileReads.Object, f.LocationReads.Object, f.HoursReads.Object,
            Mock.Of<IFaqRepository>(MockBehavior.Strict), f.OfferingReads.Object, log.Object);
        var value = await service.QueryAsync(f.Workspace, new() { WorkspaceId = f.Workspace }, new() { Topic = KnowledgeTopic.Profile }, default);
        Assert.Equal(KnowledgeStatus.Found, value.Status);
        var trace = Assert.Single(log.Invocations, i => i.Method.Name == "Log").Arguments[2].ToString()!;
        Assert.Contains("Phase=KnowledgeQuery", trace); Assert.Contains("Outcome=Found", trace);
        Assert.Contains("Reason=Profile", trace); Assert.Contains("ElapsedMs=", trace);
        Assert.DoesNotContain("PRIVATE-", trace); Assert.DoesNotContain(f.Workspace.ToString(), trace);
    }

    [Theory]
    [InlineData("clinic")] [InlineData("hotel")] [InlineData("store")]
    public async Task Informational_context_has_one_coherent_question_and_replay_has_no_second_effect(string sector)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true);
        f.UseCatalogKnowledge(); f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Profile });
        var writes = f.Firestore.CommitRpcCalls; var reads = f.Firestore.ReadRpcCalls;
        var timer = Stopwatch.StartNew(); await f.HandleTurn("¿Qué actividad desarrolla este negocio?", "public-profile"); timer.Stop();
        var measuredWrites = f.Firestore.CommitRpcCalls - writes; var measuredReads = f.Firestore.ReadRpcCalls - reads;
        Assert.InRange(measuredWrites, 1, 2); Assert.InRange(measuredReads, 1, 6);
        var state = await f.Context();
        Assert.Equal(ConversationIntent.Profile, state.CurrentQuestion!.Intent);
        Assert.Null(state.CurrentGoal); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        Assert.Equal(1, f.AiCalls); Assert.Empty(f.Wording.Invocations);
        await f.HandleTurn("¿Qué actividad desarrolla este negocio?", "public-profile");
        Assert.Single(f.Sent); Assert.Equal(1, f.AiCalls);
        output.WriteLine(JsonSerializer.Serialize(new { Metric = "RuntimeEfficiency", Sector = sector, Synthetic = true,
            FirestoreReadRpcs = measuredReads, FirestoreCommitRpcs = measuredWrites, SemanticInvocations = f.AiCalls,
            CompositionInvocations = f.Wording.Invocations.Count, ElapsedMs = timer.Elapsed.TotalMilliseconds,
            UniqueReservations = f.Bookings.Count, UniqueOrders = f.Orders.Count }));
    }
}
