using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using Fixture = NexFlow.Tests.AssistantConversationTests.Fixture;

namespace NexFlow.Tests;

public sealed class Rc3Sprint6LocalMeasurementTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Deterministic_turns_report_local_percentiles_rpc_attempts_and_no_business_effects()
    {
        var a = new Fixture(); var b = new Fixture();
        await a.Turn("Hola?", "warmup"); await b.Turn("Hola?", "warmup");
        var reads = a.Firestore.ReadRpcCalls + b.Firestore.ReadRpcCalls;
        var commits = a.Firestore.CommitRpcCalls + b.Firestore.CommitRpcCalls;
        var samples = new List<double>();
        for (var i = 0; i < 32; i++)
        {
            var start = Stopwatch.GetTimestamp();
            await Task.WhenAll(a.Turn("Hola?", "measure-" + i), b.Turn("Hola?", "measure-" + i));
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        Assert.Equal(33, a.Sent.Count); Assert.Equal(33, b.Sent.Count);
        Assert.Equal(0, a.AiCalls + b.AiCalls);
        Assert.Empty(a.Bookings); Assert.Empty(b.Bookings); Assert.Empty(a.Orders); Assert.Empty(b.Orders);
        samples.Sort(); double Percentile(double percentile) => samples[(int)Math.Ceiling(samples.Count * percentile) - 1];
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            environment = "local real orchestrator + SDK/in-memory RPC, mocked providers; NOT cloud/load/SLO proof",
            batches = samples.Count, turns = samples.Count * 2,
            batchP50Ms = Percentile(.5), batchP95Ms = Percentile(.95), batchP99Ms = Percentile(.99),
            readRpcAttempts = a.Firestore.ReadRpcCalls + b.Firestore.ReadRpcCalls - reads,
            commitRpcAttempts = a.Firestore.CommitRpcCalls + b.Firestore.CommitRpcCalls - commits,
            actualProviderCalls = 0, commercialEffects = 0
        }));
    }
}
