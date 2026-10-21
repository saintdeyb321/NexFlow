using System.Diagnostics;
using System.Text.Json;
using Moq;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class CommerceReadMetricsTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("greeting")] [InlineData("location")] [InlineData("hours")]
    [InlineData("product")] [InlineData("service")] [InlineData("booking")]
    [InlineData("order")] [InlineData("interruption")] [InlineData("ambiguous")]
    public async Task Measure_authorized_repository_calls_without_recording_customer_data(string scenario)
    {
        var f=new AssistantConversationTests.Fixture(conciergeCatalog:true,intelligentCommerce:true){Now=new(2026,10,20,21,0,0,DateTimeKind.Utc)};
        if(scenario is "booking" or "interruption") await f.HandleTurn("Quiero reservar limpieza dental en Concepción pasado mañana por la tarde","setup");
        if(scenario=="order") await f.HandleTurn("Quiero dos panes integrales","setup");
        var requests=scenario switch {
            "greeting"=>"Hola, buenas tardes", "location"=>"¿Dónde queda su sede?", "hours"=>"¿A qué hora atienden?",
            "product"=>"¿A cuánto está el pan integral?", "service"=>"¿Cuánto dura la limpieza dental?",
            "booking"=>"16:00", "order"=>"Mejor cuatro panes", "interruption"=>"¿Cuál es su horario de atención?", _=>"¿Eso cómo sería?"};
        Mock[] mocks=[f.OfferingReads,f.LocationReads,f.ProfileReads,f.HoursReads,f.EngineReads,f.EntitlementReads,f.KnowledgeReads,f.OrderWrites,f.CatalogReads];
        var starts=mocks.Select(m=>m.Invocations.Count).ToArray();var reads=f.Firestore.ReadRpcCalls;var writes=f.Firestore.CommitRpcCalls;var ai=f.AiCalls;
        var watch=Stopwatch.StartNew();await f.HandleTurn(requests,"measured");watch.Stop();
        Assert.NotEmpty(f.Sent["measured"].Content);Assert.Empty(f.Wording.Invocations);
        output.WriteLine(JsonSerializer.Serialize(new {Metric="CommerceReads",Scenario=scenario,ElapsedMs=watch.Elapsed.TotalMilliseconds,
            AiCalls=f.AiCalls-ai,FirestoreReads=f.Firestore.ReadRpcCalls-reads,FirestoreCommits=f.Firestore.CommitRpcCalls-writes,
            Calls=mocks.SelectMany((m,i)=>m.Invocations.Skip(starts[i])).GroupBy(v=>v.Method.Name).ToDictionary(g=>g.Key,g=>g.Count())}));
    }
}
