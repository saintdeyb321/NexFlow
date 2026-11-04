using Google.Api.Gax;
using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Infrastructure.Persistence.Firestore;
using Xunit;

namespace NexFlow.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class AgentJournalEmulatorTests
{
    [IsolatedIntegrationFact]
    public async Task Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone()
    {
        IntegrationSafety.Emulator(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST"), Environment.GetEnvironmentVariable("GCLOUD_PROJECT"));
        FirestoreDb Client() => new FirestoreDbBuilder { ProjectId = "demo-nexflow-rc3", EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var clock = new SyntheticClock(); var db = Client(); var a = new FirestoreAgentTurnJournal(db, clock); var b = new FirestoreAgentTurnJournal(Client(), clock);
        var ws = Guid.NewGuid(); var request = new AgentTurnRequest(ws, "synthetic-chat", "synthetic-contact", "synthetic-source", "Synthetic request");
        var options = new AgentDialogueOptions(); var context = new ConversationContextDto();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
        var results = await Task.WhenAll(a.BeginAsync(request, context, options, ct), b.BeginAsync(request, context, options, ct));
        var lease = Assert.Single(results, r => r.Lease != null).Lease!;
        var prepared = await a.CompleteAsync(request, lease, new ConversationContextDto { CurrentGoal = "RESERVATION" },
            new(true, "Prepared", null, [], 1, 0, VerifiedText: "Synthetic emulator payload"), ct);
        var durable = await new FirestoreConversationStateRepository(db).GetStateAsync(ws, request.ClientKey, ct);
        Assert.Equal(prepared.AppliedVersion, durable!.StateVersion); Assert.Equal("RESERVATION", durable.CurrentGoal);
        var replay = await b.BeginAsync(request, durable, options, ct); Assert.Null(replay.Lease); Assert.Equal("Synthetic emulator payload", replay.Result!.VerifiedText);
        clock.UtcNow = clock.UtcNow.AddHours(25); using var scope = AgentExperimentScope.Begin(ws);
        Assert.Equal(1, (await b.CompactBatchAsync(ws, null, 10, ct)).Cleared);
        Assert.Equal("ResultExpired", (await a.ObserveAsync(request, ct)).Result!.Outcome);
        Assert.Null((await b.BeginAsync(request, durable, options, ct)).Lease);
        // No PostgreSQL, migrations, credentials or production resources. The
        // isolated emulator owner may discard its process; claims aren't erased.
    }
    private sealed class SyntheticClock : NexFlow.Application.Abstractions.IClock { public DateTime UtcNow { get; set; } = DateTime.UtcNow; }
}
