using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class FirstContactTests(ITestOutputHelper output)
{
    public static readonly string[] Inputs=["Hola","Hola, buenas tardes","Buenas","Buenos días, quisiera información",
        "Hola, ¿qué servicios tienen?","Hola, quiero reservar una cita","Buenas, ¿cuánto cuesta una limpieza?",
        "Hola, necesito ayuda con un pedido","Hola, no sé cómo reservar","Hola, quiero hablar con alguien"];
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public async Task Initial_contact_handles_the_intention_without_a_forced_global_menu(int scenario)
    {
        var f=new AssistantConversationTests.Fixture(conciergeCatalog:true,intelligentCommerce:true);
        f.Services[0].PriceMinorUnits=7500;f.Services[1].IsActive=false;
        var watch=Stopwatch.StartNew();var outcome="Passed";
        try {
            await f.HandleTurn(Inputs[scenario],"first");var text=f.Sent.GetValueOrDefault("first")?.Content??"";
            ExportSynthetic(scenario,text);
            Check(text.Length>0,"Response");Check(!text.Contains("estas opciones",StringComparison.OrdinalIgnoreCase),"NoGlobalMenu");
            // A decimal price in a factual reply is not a numbered choice; match actual list lines.
            if(scenario<=3) Check(!Regex.IsMatch(text,@"(?m)^\s*\d+[.)]\s+"),"NoForcedNumericChoice");
            if(scenario==4) Check(text.Contains("Limpieza dental",StringComparison.OrdinalIgnoreCase),"Services");
            if(scenario==5) Check((await f.Context()).CurrentGoal=="RESERVATION","BookingDraft");
            if(scenario==6) Check(text.Contains("75.00",StringComparison.OrdinalIgnoreCase),"VerifiedPrice");
            if(scenario==7) Check(text.Contains("pedido",StringComparison.OrdinalIgnoreCase) && (await f.Context()).CurrentGoal==null,"OrderHelpNotPurchase");
            if(scenario==8) Check(text.Contains("Para reservar",StringComparison.OrdinalIgnoreCase),"ReservationInstruction");
            Check(f.Handoffs==(scenario==9 ? 1 : 0),"HumanRequest");Check(f.AiCalls==0,"NoUnnecessaryAi");
            Check(f.Orders.Count==0 && f.Bookings.Count==0,"NoPrematureEffect");
        } catch {outcome="Failed";throw;}
        finally {
            watch.Stop();output.WriteLine(JsonSerializer.Serialize(new{Metric="FirstContact",Case=$"W{scenario+1:00}",DurationMs=watch.Elapsed.TotalMilliseconds,
                Processed=1,NoResponse=f.Sent.Count==0 ? 1 : 0,SemanticCalls=f.AiCalls,CompositionCalls=f.Wording.Invocations.Count,
                FirestoreReads=f.Firestore.ReadRpcCalls,FirestoreCommits=f.Firestore.CommitRpcCalls,
                Orders=f.Orders.Count,Reservations=f.Bookings.Count,Handoffs=f.Handoffs,Delivery=f.Sent.GetValueOrDefault("first")?.Status.ToString(),Outcome=outcome}));
        }
        void Check(bool ok,string rule)=>Assert.True(ok,$"W{scenario+1:00}: {rule}");
    }
    private static void ExportSynthetic(int scenario,string response)
    {
        var path=Environment.GetEnvironmentVariable("NEXFLOW_SYNTHETIC_REVIEW_OUTPUT");if(string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        // Dedicated source-controlled synthetic cases; never operational/customer traces.
        File.WriteAllText(Path.Combine(path,$"W{scenario+1:00}.json"),JsonSerializer.Serialize(new {Case=$"W{scenario+1:00}",Synthetic=true,Input=Inputs[scenario],Response=response}));
    }

    [Fact]
    public async Task Greeting_replay_and_repeat_do_not_reintroduce_a_welcome_or_spend_ai()
    {
        var f = new AssistantConversationTests.Fixture();
        await f.HandleTurn("Hola", "first");
        var first = f.Sent["first"].Content;
        await f.HandleTurn("Hola", "first");
        Assert.Single(f.Sent);
        await f.HandleTurn("Buenas", "next");
        Assert.DoesNotContain("Hola", f.Sent["next"].Content);
        Assert.NotEqual(first, f.Sent["next"].Content);
        Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData("ORDER")]
    [InlineData("RESERVATION")]
    public async Task Active_draft_greeting_only_resumes_and_preserves_business_data(string goal)
    {
        var f = new AssistantConversationTests.Fixture();
        await f.SeedDraft(goal);
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await f.HandleTurn("Hola, buenas tardes", "resume");
        Assert.DoesNotContain("Hola", f.Sent["resume"].Content);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal(0, f.AiCalls);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task Restricted_greeting_intention_never_reads_unlicensed_offerings()
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true);
        f.Modules.Clear();
        await f.HandleTurn("Hola", "welcome");
        Assert.Empty(f.OfferingReads.Invocations);
        await f.HandleTurn("Hola, ¿qué servicios tienen?", "intent");
        Assert.Empty(f.OfferingReads.Invocations);
        Assert.Empty(f.FactualQueries);
        Assert.DoesNotContain("Limpieza dental", f.Sent["intent"].Content);
        Assert.Equal(0, f.AiCalls);
    }
}
