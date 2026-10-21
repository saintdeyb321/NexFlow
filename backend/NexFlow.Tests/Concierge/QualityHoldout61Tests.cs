using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

// Frozen before the welcome correction. Failures are evidence, never training examples.
public sealed class QualityHoldout61Tests(ITestOutputHelper output)
{
    private static readonly Conversation[] Corpus = Load();
    public static IEnumerable<object[]> Cases() => Corpus.Select(c => new object[] { c.Id });
    private static Conversation[] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NexFlow.Tests.Concierge.QualityHoldout61.json")!;
        return JsonSerializer.Deserialize<Conversation[]>(stream)!;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Frozen_independent_conversation(string id)
    {
        var script = Corpus.Single(c => c.Id == id);
        var failures = new List<string>();
        await Evaluate(false);
        if (script.OtherWorkspace) await Evaluate(true);
        Assert.True(failures.Count == 0, string.Join("; ", failures));

        async Task Evaluate(bool other)
        {
            var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true) {
                Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc)
            };
            f.Services[0].PriceMinorUnits = 7500;
            f.UseCatalogKnowledge();
            if (script.Modules != null) f.Modules.IntersectWith(script.Modules);
            if (script.InactiveBread) f.Products[0].IsActive = false;
            if (script.MixedCurrency) f.Products[1].Currency = "USD";
            if (other) f.Products[0].PriceMinorUnits = 999;
            var index = 0;
            foreach (var turn in script.Turns)
            {
                index++;
                if (turn.Revoke != null) f.Modules.Remove(turn.Revoke);
                if (turn.Semantic != null) f.Interpretations.Enqueue(new() {
                    Intent = Enum.Parse<ConversationIntent>(turn.Semantic),
                    QueryKind = turn.Broad ? OfferingQueryKind.Broad : OfferingQueryKind.Specific
                });
                if (turn.Provider != null)
                {
                    var router = new Mock<IAiRouter>();
                    var call = router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()));
                    if (turn.Provider == "Timeout") call.ThrowsAsync(new TimeoutException());
                    else call.ReturnsAsync("{invalid-json");
                    var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(f.Now);
                    var profiles = new Mock<IBusinessProfileRepository>();
                    profiles.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new BusinessProfileDto("Synthetic", "", "", "", "", "America/Lima"));
                    var interpreter = new AiInterpreter(router.Object, clock.Object, profiles.Object, NullLogger<AiInterpreter>.Instance);
                    f.InterpretationOverride = () => interpreter.InterpretAsync(f.Workspace, turn.Text, "", null, f.Modules, default);
                }
                var prior = await f.Context(); var before = AssistantConversationTests.Fixture.BusinessDraft(prior);
                var source = turn.Source ?? "holdout-" + index;
                var sent = f.Sent.Count; var ai = f.AiCalls; var wording = f.Wording.Invocations.Count;
                var knowledge = f.KnowledgeReads.Invocations.Count; var understood = f.Understood.Count;
                var reads = f.Firestore.ReadRpcCalls; var writes = f.Firestore.CommitRpcCalls;
                var orders = f.Orders.Count; var bookings = f.Bookings.Count; var human = f.Handoffs;
                Mock[] repositories = [f.OfferingReads, f.LocationReads, f.ProfileReads, f.HoursReads, f.EntitlementReads, f.KnowledgeReads, f.OrderWrites, f.EngineReads];
                var offsets = repositories.Select(m => m.Invocations.Count).ToArray();
                var issues = new List<string>(); var watch = Stopwatch.StartNew(); string? error = null;
                try
                {
                    await f.HandleTurn(turn.Text, source);
                    watch.Stop();
                }
                catch (Exception ex) { error = ex.GetType().Name; issues.Add("HandlerError"); }
                // Exclude diagnostic reads of state from measured SDK operations.
                var measuredReads = f.Firestore.ReadRpcCalls - reads; var measuredWrites = f.Firestore.CommitRpcCalls - writes;
                var state = await f.Context(); var response = f.Sent.GetValueOrDefault(source)?.Content ?? "";
                Check(turn.Silent ? sent == f.Sent.Count : response.Length > 0, "ResponsePresence");
                foreach (var fact in turn.Contains) Check(response.Contains(other ? fact.Replace("2.50", "9.99") : fact, StringComparison.OrdinalIgnoreCase), "VerifiedFact");
                if (turn.Preserve) Check(before == AssistantConversationTests.Fixture.BusinessDraft(state), "DraftPreservation");
                if (turn.Quantities != null) Check(state.OrderDraftItems.Count == turn.Quantities.Count && state.OrderDraftItems.All(i => turn.Quantities.GetValueOrDefault(i.ProductId) == i.Quantity), "Quantities");
                if (turn.Goal != null) Check(state.CurrentGoal == turn.Goal, "Goal");
                if (turn.Date != null) Check(state.TargetDate == turn.Date, "Date");
                if (turn.Step != null) Check(state.CurrentStep == turn.Step, "Step");
                if (turn.EmptyDraft) Check(state.OrderDraftItems.Count == 0, "EmptyDraft");
                Check(f.Orders.Count == (turn.Orders ?? orders), "OrderEffects");
                Check(f.Bookings.Count == (turn.Bookings ?? bookings), "ReservationEffects");
                Check(f.Handoffs == (turn.Human ? 1 : 0), "HumanPriority");
                if (turn.NoKnowledge) Check(f.KnowledgeReads.Invocations.Count == knowledge, "RestrictedRead");
                if (turn.Provider != null) Check(f.Understood.Skip(understood).Any(t => t.Act == DialogueAct.ProviderFailure), "ProviderFailureClassified");
                if (!turn.OptionalMenu) Check(!response.Contains("Estas son las opciones disponibles", StringComparison.OrdinalIgnoreCase)
                    && !response.Contains("Puedo ayudarte con estas opciones", StringComparison.OrdinalIgnoreCase), "NoForcedGlobalMenu");
                Check(repositories.SelectMany((m, i) => m.Invocations.Skip(offsets[i])).All(call => call.Arguments.Count == 0 || call.Arguments[0] is not Guid ws || ws == f.Workspace), "WorkspaceIsolation");
                Check(f.Wording.Invocations.Count == wording, "NoCourtesyAi");
                LinguisticTrace.Write(id, index, f, prior, state, understood, turn.Text, response, issues);
                watch.Stop();
                output.WriteLine(JsonSerializer.Serialize(new {
                    Metric = "QualityHoldout61", Case = id, Turn = index, Scope = other ? "B" : "A", Synthetic = true,
                    DurationMs = watch.Elapsed.TotalMilliseconds, Processed = error == null ? 1 : 0, Error = error,
                    NoResponse = f.Sent.Count == sent && !turn.Silent && turn.Source == null ? 1 : 0,
                    IntentionalSuppression = turn.Silent, SemanticCalls = f.AiCalls - ai, CompositionCalls = f.Wording.Invocations.Count - wording,
                    SemanticStub = turn.Semantic != null, ProviderFailureStub = turn.Provider != null,
                    FirestoreReads = measuredReads, FirestoreCommits = measuredWrites,
                    Calls = repositories.SelectMany((m, i) => m.Invocations.Skip(offsets[i])).GroupBy(c => c.Method.Name).ToDictionary(g => g.Key, g => g.Count()),
                    Orders = f.Orders.Count - orders, Reservations = f.Bookings.Count - bookings, Handoffs = f.Handoffs - human,
                    Delivery = f.Sent.Count == sent ? "NoNewDelivery" : f.Sent.GetValueOrDefault(source)?.Status.ToString(),
                    Outcome = issues.Count == 0 ? "Passed" : "Failed", Criteria = issues
                }));
                failures.AddRange(issues.Select(rule => $"{id}/{index}/{(other ? "B" : "A")}: {rule}"));
                f.InterpretationOverride = null;
                void Check(bool ok, string rule) { if (!ok) issues.Add(rule); }
            }
            CheckFinal(f.OrderWrites.Invocations.Count(i => i.Method.Name == "CreateOrderAsync") == f.Orders.Count, "NoDuplicateOrderAttempt");
            CheckFinal(f.EngineReads.Invocations.Count(i => i.Method.Name == "CreateReservationAsync") == f.Bookings.Count, "NoDuplicateReservationAttempt");
            void CheckFinal(bool ok, string rule) { if (!ok) failures.Add(id + ": " + rule); }
        }
    }

    public sealed record Conversation(string Id, string Purpose, Turn[] Turns, string[]? Modules = null, bool InactiveBread = false, bool MixedCurrency = false, bool OtherWorkspace = false);
    public sealed class Turn
    {
        public string Text { get; set; } = "";
        public string[] Contains { get; set; } = [];
        public Dictionary<string, int>? Quantities { get; set; }
        public string? Goal { get; set; }
        public string? Date { get; set; }
        public string? Step { get; set; }
        public string? Source { get; set; }
        public string? Revoke { get; set; }
        public string? Semantic { get; set; }
        public string? Provider { get; set; }
        public bool Broad { get; set; }
        public bool Preserve { get; set; }
        public bool EmptyDraft { get; set; }
        public bool Human { get; set; }
        public bool Silent { get; set; }
        public bool NoKnowledge { get; set; }
        public bool OptionalMenu { get; set; }
        public int? Orders { get; set; }
        public int? Bookings { get; set; }
    }
}
