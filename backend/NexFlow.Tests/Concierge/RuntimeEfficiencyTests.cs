using System.Diagnostics;
using System.Text.Json;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class RuntimeEfficiencyTests(ITestOutputHelper output)
{
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
