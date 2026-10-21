using System.Diagnostics;
using System.Text.Json;
using Moq;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

// Synthetic providers; real handler, recovery, pipeline, booking/chat flows and composer.
public sealed class ComposedConversationTests(ITestOutputHelper output)
{
    private static AssistantConversationTests.Fixture Create(bool writing = true)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true)
            { Now = new DateTime(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc) };
        f.CompositionOptions.EnableAi = writing;
        f.Wording.Setup(r => r.ExecuteTaskAsync(AiTaskType.ComplexChat, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"Lead\":\"Perfecto.\"}");
        return f;
    }

    public static IEnumerable<object[]> NegativeCases() =>
        from text in new[] { "no quisiera reservar todavía", "solo estoy preguntando", "no quiero esa cita", "quiero reservar limpieza dental y comprar un producto" }
        from provider in new[] { "available", "invalid", "down" }
        from active in new[] { false, true }
        select new object[] { text, provider, active };

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task Negations_and_conflicting_intents_never_create_or_silently_change_a_draft(string text, string provider, bool active)
    {
        var f = Create(false);
        if (active)
        {
            await f.HandleTurn("quiero limpieza dental en Concepción pasado mañana a las 16:00", "seed");
            Assert.Null((await f.Context()).RealCustomerName);
        }
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(provider switch {
            "down" => new() { Intent = ConversationIntent.ProviderUnavailable },
            "invalid" => new() { Intent = ConversationIntent.General, RequiresClarification = true },
            _ => new() { Intent = ConversationIntent.Reservation, Act = DialogueAct.StartOperation,
                Service = "Limpieza dental", CustomerName = "Persona Inventada", Evidence = [new("Act", text, "StartOperation")] }
        });
        await f.HandleTurn(text, "negative");
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders); Assert.Equal(0, f.Handoffs);
        Assert.True(f.AiCalls <= 1); Assert.Empty(f.Wording.Invocations);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    [InlineData(13)] [InlineData(14)] [InlineData(15)] [InlineData(16)]
    public async Task Required_end_to_end_scenarios(int scenario)
    {
        var f = Create();
        switch (scenario)
        {
            case 1:
                await Turn("Hola"); Assert.Contains("Hola", f.Sent.Values.Last().Content); Assert.Equal(0, f.AiCalls); break;
            case 2:
                f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProductQuery, SearchTerm = "Producto real", QueryKind = OfferingQueryKind.Specific });
                await Turn("¿Cuánto cuesta el Producto real?"); Assert.Single(f.FactualQueries); Assert.Contains("25.00", f.Sent.Values.Last().Content); break;
            case 3:
                f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ServiceQuery, SearchTerm = "Limpieza dental", QueryKind = OfferingQueryKind.Specific });
                await Turn("¿Cuánto cuesta la limpieza dental?"); Assert.Contains("25.00", f.Sent.Values.Last().Content); Assert.Single(f.FactualQueries); break;
            case 4:
                await Turn("Quiero reservar una limpieza dental para pasado mañana");
                Assert.Contains("Limpieza dental", f.Sent.Values.Last().Content); Assert.Contains("jueves 22", f.Sent.Values.Last().Content); break;
            case 5:
                await Turn("Quiero reservar limpieza dental para pasado mañana"); await Turn("En Concepción, mejor por la tarde");
                Assert.Equal("sede-a", (await f.Context()).SelectedLocationId); Assert.Contains("16:00", f.Sent.Values.Last().Content); break;
            case 6: case 7: case 8:
                await Turn("quiero limpieza dental en Concepción pasado mañana por la tarde"); await Turn("16:00");
                Assert.Equal("COLLECT_CUSTOMERNAME", (await f.Context()).CurrentStep); Assert.Empty(f.Bookings);
                if (scenario >= 7) { await Turn("Ana Pérez"); Assert.Empty(f.Bookings); Assert.Equal(OperationStatus.AwaitingConfirmation, f.Plans.Last().Operation); }
                if (scenario == 8) { await Turn("sí", "commit"); await Turn("sí", "commit"); Assert.Single(f.Bookings); Assert.Equal(OperationStatus.Succeeded, f.Plans.Last().Operation);
                    Assert.Equal("America/Lima", f.Plans.Last().TimeZone); Assert.Equal("PEN", f.Plans.Last().Currency);
                    Assert.Contains(f.Plans.Last().Facts, fact => fact.Key == "ReservationStatus" && fact.Source == VerifiedFactSource.TransactionEngine); }
                break;
            case 9:
                await Turn("quiero limpieza dental en Concepción pasado mañana");
                var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
                f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ServiceQuery, SearchTerm = "Limpieza dental", QueryKind = OfferingQueryKind.Specific });
                await Turn("¿Cuánto cuesta ese servicio?"); Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Single(f.FactualQueries); break;
            case 10:
                await Turn("quiero limpieza dental en Concepción");
                var draft = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
                f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Order }); await Turn("mejor quiero comprar un producto");
                Assert.Equal(draft, AssistantConversationTests.Fixture.BusinessDraft(await f.Context())); Assert.Empty(f.Orders); break;
            case 11:
                f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation }); await Turn("no quisiera reservar todavía");
                Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings); break;
            case 12:
                await Turn("quiero limpieza dental en Concepción");
                f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProviderUnavailable }); await Turn("tal vez podríamos organizar algo");
                Assert.Contains("fecha", f.Sent.Values.Last().Content); Assert.DoesNotContain("1.", f.Sent.Values.Last().Content);
                Assert.Equal(0, f.Handoffs); Assert.Equal(0, (await f.Context()).ClarificationAttempts); break;
            case 13:
                f.Wording.Setup(r => r.ExecuteTaskAsync(AiTaskType.ComplexChat, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
                    .ReturnsAsync("{\"Lead\":\"Reserva confirmada gratis a las 03:00\"}");
                await Turn("quiero limpieza dental en Concepción pasado mañana");
                Assert.EndsWith(f.Plans.Last().FallbackText, f.Sent.Values.Last().Content); Assert.Empty(f.Bookings); Assert.Empty(f.Wording.Invocations); break;
            case 14:
                f.Modules.Remove("CATALOG"); f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProductQuery });
                await Turn("¿Qué productos tienen?"); Assert.Empty(f.FactualQueries); Assert.Empty(f.Wording.Invocations); break;
            case 15:
                await Turn("quiero limpieza dental en Concepción"); var calls = f.Wording.Invocations.Count;
                await Turn("quiero hablar con una persona", "human"); await Turn("quiero hablar con una persona", "human");
                Assert.Equal(1, f.Handoffs); Assert.Equal("Human", (await f.Context()).Mode); Assert.Equal(calls, f.Wording.Invocations.Count); break;
            case 16:
                f.DeliveryStatus = MessageStatus.UnknownDelivery;
                await Turn("Hola", "uncertain"); var plans = f.Plans.Count;
                await Turn("Hola", "uncertain"); Assert.Single(f.Sent); Assert.Equal(plans, f.Plans.Count);
                Assert.Equal(MessageStatus.UnknownDelivery, f.Sent["uncertain"].Status); break;
        }
        Assert.All(f.Plans, p => Assert.Equal(f.Workspace, p.WorkspaceId));

        async Task Turn(string text, string? source = null)
        {
            source ??= "phase4-" + f.Sent.Count;
            var calls = f.AiCalls; var writing = f.Wording.Invocations.Count; var count = f.Plans.Count;
            var repositories = new Moq.Mock[] { f.ProfileReads, f.LocationReads, f.OfferingReads, f.HoursReads, f.EntitlementReads, f.KnowledgeReads, f.EngineReads };
            var offsets = repositories.Select(r => r.Invocations.Count).ToArray();
            var reads = f.Firestore.ReadRpcCalls; var commits = f.Firestore.CommitRpcCalls;
            var watch = Stopwatch.StartNew();
            await f.HandleTurn(text, source); var plan = f.Plans.Count > count ? f.Plans.Last() : null;
            var response = f.Sent.GetValueOrDefault(source)?.Content;
            output.WriteLine(JsonSerializer.Serialize(new { Scenario = scenario, Source = source, Input = text,
                DeterministicInput = plan?.FallbackText, Response = response, Act = plan?.Act.ToString(), Operation = plan?.Operation.ToString(),
                Facts = plan?.Facts, InterpretationCalls = f.AiCalls - calls, WordingCalls = f.Wording.Invocations.Count - writing,
                ElapsedMilliseconds = watch.ElapsedMilliseconds, Questions = response?.Count(c => c == '?'),
                ExactBodyPreserved = plan == null || response?.EndsWith(plan.FallbackText, StringComparison.Ordinal) == true,
                FirestoreReads = f.Firestore.ReadRpcCalls - reads, FirestoreCommits = f.Firestore.CommitRpcCalls - commits,
                RepositoryOperations = repositories.SelectMany((r, i) => r.Invocations.Skip(offsets[i]).Select(v => v.Method.Name)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()) }));
            if (plan != null) Assert.EndsWith(plan.FallbackText, response);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Revocation_or_human_takeover_during_writing_cannot_leak_a_stale_response(bool human)
    {
        var f = Create();
        f.DuringComposition = async () => {
            if (human) { var c = await f.Context(); c.Mode = "Human"; await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, c, default); }
            else f.Modules.Remove("SERVICES");
        };
        if (human) { await Assert.ThrowsAsync<ConcurrencyException>(() => f.HandleTurn("quiero limpieza dental en Concepción", "race")); Assert.Empty(f.Sent); }
        else { await f.HandleTurn("quiero limpieza dental en Concepción", "race"); Assert.DoesNotContain("Limpieza dental", f.Sent["race"].Content); }
        Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Concurrent_workspaces_have_detached_plans_and_separate_sources()
    {
        var first = Create(); var second = Create();
        await Task.WhenAll(first.HandleTurn("quiero limpieza dental en Concepción pasado mañana", "same-source"),
            second.HandleTurn("quiero limpieza profunda en San Jerónimo mañana", "same-source"));
        Assert.All(first.Plans, p => Assert.Equal(first.Workspace, p.WorkspaceId));
        Assert.All(second.Plans, p => Assert.Equal(second.Workspace, p.WorkspaceId));
        Assert.NotEqual(first.Workspace, second.Workspace); Assert.NotSame(first.Plans.Last(), second.Plans.Last());
        Assert.Equal("sede-a", (await first.Context()).SelectedLocationId); Assert.Equal("sede-b", (await second.Context()).SelectedLocationId);
        Assert.Equal("2026-10-22", (await first.Context()).TargetDate); Assert.Equal("2026-10-21", (await second.Context()).TargetDate);
    }
}
