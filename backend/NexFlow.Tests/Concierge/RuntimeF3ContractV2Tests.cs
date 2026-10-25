using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

// A separately versioned UX contract. Neither historical corpus nor evaluator is modified.
public sealed class RuntimeF3ContractV2Tests
{
    public static IEnumerable<object[]> Cases() => new[] { "clinic", "hotel", "restaurant", "bakery", "limited" }
        .SelectMany(sector => new[] { sector + "-09", sector + "-10" }).Concat(["H10", "H21"])
        .Select(id => new object[] { id });

    private static T[] Load<T>(string resource)
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("NexFlow.Tests.Concierge." + resource)!;
        return JsonSerializer.Deserialize<T[]>(input)!;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Preferred_time_contract_v2_preserves_the_original_business_and_safety_assertions(string id)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true) {
            Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc)
        };
        var publicCase = Load<CommercialCertificationTests.CertificationConversation>("CommercialCertificationCorpus.json").SingleOrDefault(c => c.Id == id);
        IEnumerable<Step> steps;
        if (publicCase != null)
        {
            for (var i = 0; i < 3; i++) f.Products[i].Name = publicCase.Products[i];
            for (var i = 0; i < 2; i++) f.Services[i].Name = publicCase.Services[i];
            f.Services[0].PriceMinorUnits = 6000;
            if (publicCase.Sector == "limited") f.Modules.IntersectWith(["CATALOG", "ORDERS", "SERVICES", "RESERVATIONS"]);
            steps = publicCase.Turns.Select(t => new Step(t.Text, t.Contains, t.Goal, t.Step, t.Date, t.Location,
                t.TimeMissing, t.Preserve, t.Bookings, t.Orders, t.Intent));
        }
        else
        {
            var frozen = Load<QualityHoldout61Tests.Conversation>("QualityHoldout61.json").Single(c => c.Id == id);
            f.Services[0].PriceMinorUnits = 7500; f.UseCatalogKnowledge();
            steps = frozen.Turns.Select(t => new Step(t.Text, t.Contains, t.Goal, t.Step, t.Date, null,
                false, t.Preserve, t.Bookings, t.Orders, null, t.Provider));
        }
        var replacements = 0; var index = 0;
        foreach (var step in steps)
        {
            if (step.Provider != null)
            {
                var router = new Mock<IAiRouter>();
                router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new TimeoutException());
                var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(f.Now);
                var interpreter = new AiInterpreter(router.Object, clock.Object, f.ProfileReads.Object, NullLogger<AiInterpreter>.Instance);
                f.InterpretationOverride = () => interpreter.InterpretAsync(f.Workspace, step.Text, "", null, f.Modules, default);
            }
            var prior = await f.Context(); var before = AssistantConversationTests.Fixture.BusinessDraft(prior);
            var bookings = f.Bookings.Count; var source = "v2-" + ++index;
            await f.HandleTurn(step.Text, source); f.InterpretationOverride = null;
            var state = await f.Context(); var reply = f.Sent[source].Content;
            // The ONLY replacement is the obsolete automatic grid assertion on a period.
            var preferredQuestion = step.Contains.Contains("16:00") && state.CurrentStep == "COLLECT_TIME" && state.PreferredTime != null;
            foreach (var fact in step.Contains.Where(fact => !preferredQuestion || fact != "16:00"))
                Assert.Contains(fact, reply, StringComparison.OrdinalIgnoreCase);
            if (step.Goal != null) Assert.Equal(step.Goal, state.CurrentGoal);
            if (step.Phase != null) Assert.Equal(step.Phase, state.CurrentStep);
            if (step.Date != null) Assert.Equal(step.Date, state.TargetDate);
            if (step.Location != null) Assert.Equal(step.Location, state.SelectedLocationId);
            if (step.TimeMissing) Assert.Null(state.TargetTime);
            if (step.Preserve) Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(state));
            if (step.Bookings.HasValue) Assert.Equal(step.Bookings, f.Bookings.Count);
            if (step.Orders.HasValue) Assert.Equal(step.Orders, f.Orders.Count);
            if (step.Intent != null) Assert.Equal(step.Intent, f.Understood.Last().Interpretation.Intent.ToString());
            if (step.Provider != null) Assert.Equal(DialogueAct.ProviderFailure, f.Understood.Last().Act);
            if (f.Bookings.Count > bookings)
            {
                Assert.Equal("CONFIRM", prior.CurrentStep);
                Assert.Equal(ExpectedReply.ConfirmReservation, prior.CurrentQuestion!.Expected);
                Assert.Contains(step.Text.ToLowerInvariant(), new[] { "sí", "sí confirmo" });
            }
            if (preferredQuestion)
            {
                replacements++; Assert.Null(state.TargetTime); Assert.Empty(state.RecoveryOptions);
                Assert.Contains("hora", reply, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("16:00", reply);
                Assert.Equal(bookings, f.Bookings.Count);
                await f.HandleTurn("muéstrame opciones", source + "-options");
                Assert.Contains("16:00", f.Sent[source + "-options"].Content);
                Assert.Null((await f.Context()).TargetTime); Assert.Equal(bookings, f.Bookings.Count);
                Assert.Contains(f.EngineReads.Invocations, i => i.Method.Name == "GetAvailabilityAsync");
            }
        }
        Assert.Equal(1, replacements);
        // Even cases ending at the time question must verify an exact point before selection.
        if ((await f.Context()).CurrentStep == "COLLECT_TIME")
        {
            await f.HandleTurn("16:00", "v2-point");
            Assert.Equal("16:00", (await f.Context()).TargetTime);
        }
        Assert.Contains(f.EngineReads.Invocations, i => i.Method.Name == "GetAvailabilityAtAsync");
        Assert.Equal(f.Bookings.Count, f.EngineReads.Invocations.Count(i => i.Method.Name == "CreateReservationAsync"));
        Assert.Empty(f.Orders); Assert.Equal(0, f.Handoffs); Assert.Empty(f.Wording.Invocations);
        Assert.All(f.OfferingReads.Invocations.Concat(f.EngineReads.Invocations).Concat(f.LocationReads.Invocations),
            call => Assert.Equal(f.Workspace, call.Arguments[0]));
    }

    private sealed record Step(string Text, string[] Contains, string? Goal, string? Phase, string? Date,
        string? Location, bool TimeMissing, bool Preserve, int? Bookings, int? Orders, string? Intent, string? Provider = null);
}
