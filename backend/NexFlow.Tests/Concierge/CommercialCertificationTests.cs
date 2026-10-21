using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class CommercialCertificationTests(ITestOutputHelper output)
{
    private static readonly CertificationConversation[] Corpus=Load();
    public static IEnumerable<object[]> Conversations()=>Corpus.Select(c=>new object[]{c.Id});
    private static CertificationConversation[] Load()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("NexFlow.Tests.Concierge.CommercialCertificationCorpus.json")!;
        return JsonSerializer.Deserialize<CertificationConversation[]>(stream)!;
    }

    [Theory]
    [MemberData(nameof(Conversations))]
    public async Task Independent_commercial_conversation(string caseId)
    {
        var script=Corpus.Single(c=>c.Id==caseId);
        var f=new AssistantConversationTests.Fixture(conciergeCatalog:true,intelligentCommerce:true){Now=new(2026,10,20,21,0,0,DateTimeKind.Utc)};
        for(var i=0;i<3;i++) f.Products[i].Name=script.Products[i];
        for(var i=0;i<2;i++) f.Services[i].Name=script.Services[i];
        f.Services[0].PriceMinorUnits=6000;
        // Limited license still permits internal locations/hours for authorized reservations.
        if(script.Sector=="limited") f.Modules.IntersectWith(["CATALOG","ORDERS","SERVICES","RESERVATIONS"]);
        if(script.AmbiguousLocations) f.LocationReads.Setup(r=>r.GetLocationsAsync(f.Workspace,It.IsAny<CancellationToken>())).ReturnsAsync(new[]{
            new LocationDto("sede-a","Sede Centro Concepción","Synthetic",null,null,true),
            new LocationDto("sede-b","Sede Centro San Jerónimo","Synthetic",null,null,false)});
        f.KnowledgeReads.Setup(k=>k.QueryAsync(f.Workspace,It.IsAny<BusinessKnowledgeSnapshot>(),It.IsAny<KnowledgeQuery>(),It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _,BusinessKnowledgeSnapshot _,KnowledgeQuery q,CancellationToken _)=>new KnowledgeResult{
                Status=KnowledgeStatus.Found,Source=q.Topic,Facts=q.Topic==KnowledgeTopic.BusinessHours ? "Atención de 08:00 a 18:00." : "Información sintética autorizada."});
        var workspaceHash=Hash(f.Workspace.ToString());var index=0;
        foreach(var turn in script.Turns)
        {
            var source=turn.Source ?? "cert-"+ ++index;
            if(turn.SemanticIntent!=null) f.Interpretations.Enqueue(new(){Intent=Enum.Parse<ConversationIntent>(turn.SemanticIntent)});
            if(turn.Action=="deactivate") f.Products[0].IsActive=false;
            if(turn.Action=="revoke") f.Modules.Remove("ORDERS");
            if(turn.Action is "timeout" or "invalid-json")
            {
                var router=new Mock<IAiRouter>();
                var setup=router.Setup(r=>r.ExecuteTaskAsync(AiTaskType.IntentExtraction,It.IsAny<string>(),It.IsAny<string>(),true,It.IsAny<CancellationToken>()));
                if(turn.Action=="timeout") setup.ThrowsAsync(new TimeoutException());else setup.ReturnsAsync("{invalid-json");
                var clock=new Mock<IClock>();clock.SetupGet(c=>c.UtcNow).Returns(f.Now);
                var profile=new Mock<IBusinessProfileRepository>();profile.Setup(p=>p.GetProfileAsync(f.Workspace,It.IsAny<CancellationToken>())).ReturnsAsync(new BusinessProfileDto("Synthetic","","","","","America/Lima"));
                var interpreter=new AiInterpreter(router.Object,clock.Object,profile.Object,NullLogger<AiInterpreter>.Instance);
                f.InterpretationOverride=()=>interpreter.InterpretAsync(f.Workspace,turn.Text,"RESERVATION","COLLECT_TIME",f.Modules,default);
            }
            var prior=await f.Context();var before=AssistantConversationTests.Fixture.BusinessDraft(prior);var ai=f.AiCalls;
            var priorOrders=f.Orders.Count;var priorBookings=f.Bookings.Count;var knowledge=f.KnowledgeReads.Invocations.Count;
            var prepared=f.Understood.Count;var sent=f.Sent.Count;var reads=f.Firestore.ReadRpcCalls;var commits=f.Firestore.CommitRpcCalls;
            Mock[] mocks=[f.OfferingReads,f.LocationReads,f.EngineReads,f.OrderWrites,f.EntitlementReads,f.KnowledgeReads];
            var offsets=mocks.Select(m=>m.Invocations.Count).ToArray();var watch=Stopwatch.StartNew();var outcome="Passed";
            long measuredReads=0,measuredCommits=0;
            try
            {
                await f.HandleTurn(turn.Text,source);
                watch.Stop();measuredReads=f.Firestore.ReadRpcCalls-reads;measuredCommits=f.Firestore.CommitRpcCalls-commits;
                var state=await f.Context();var response=f.Sent.GetValueOrDefault(source)?.Content;
                Check(turn.Silent ? f.Sent.Count==sent : !string.IsNullOrWhiteSpace(response),"ResponsePresence");
                foreach(var fact in turn.Contains) Check(response?.Contains(fact,StringComparison.OrdinalIgnoreCase)==true,"VerifiedFact");
                foreach(var fact in turn.NotContains) Check(response?.Contains(fact,StringComparison.OrdinalIgnoreCase)!=true,"InventedFact");
                if(turn.Preserve) Check(before==AssistantConversationTests.Fixture.BusinessDraft(state),"DraftPreservation");
                if(turn.EmptyDraft) Check(state.OrderDraftItems.Count==0,"EmptyDraft");
                if(turn.Quantities!=null) Check(state.OrderDraftItems.Count==turn.Quantities.Count && state.OrderDraftItems.All(i=>turn.Quantities.GetValueOrDefault(i.ProductId)==i.Quantity),"ExactQuantities");
                if(turn.Goal!=null || turn.CheckGoal) Check(state.CurrentGoal==turn.Goal,"Goal");
                if(turn.Step!=null) Check(state.CurrentStep==turn.Step,"Step");
                if(turn.Date!=null) Check(state.TargetDate==turn.Date,"BusinessDate");
                if(turn.Location!=null) Check(state.SelectedLocationId==turn.Location,"Location");
                if(turn.LocationMissing) Check(state.SelectedLocationId==null,"AmbiguousLocation");
                if(turn.TimeMissing) Check(state.TargetTime==null,"TimeNotInvented");
                if(turn.Orders!=null) Check(f.Orders.Count==turn.Orders,"OrderEffects");
                if(turn.Bookings!=null) Check(f.Bookings.Count==turn.Bookings,"ReservationEffects");
                Check(f.Handoffs==(turn.Human ? 1 : 0),"HumanPriority");
                if(turn.ProviderFailure) Check(f.Understood.Skip(prepared).Any(p=>p.Act==DialogueAct.ProviderFailure && p.Interpretation.Intent==ConversationIntent.ProviderUnavailable),"ProviderNotCustomerFault");
                if(turn.Intent!=null && f.Understood.Count>prepared) Check(f.Understood[^1].Interpretation.Intent.ToString()==turn.Intent,"Intent");
                if(turn.NoKnowledgeRead) Check(f.KnowledgeReads.Invocations.Count==knowledge,"RestrictedKnowledgeNotRead");
                if(f.Orders.Count>priorOrders) Check(f.Modules.IsSupersetOf(["CATALOG","ORDERS"]) && (OrderMentionResolver.IsSubmit(turn.Text)
                    || prior.CurrentStep?.StartsWith("ORDER_CONFIRM:",StringComparison.Ordinal)==true && ConversationRecoveryService.Normalize(turn.Text) is "si" or "si confirmo"),"ValidOrderConsentAndLicense");
                if(f.Bookings.Count>priorBookings) Check(f.Modules.IsSupersetOf(["SERVICES","RESERVATIONS"]) && prior.CurrentStep=="CONFIRM"
                    && ConversationRecoveryService.Normalize(turn.Text) is "si" or "si confirmo","ValidReservationConsentAndLicense");
                if(state.CurrentGoal!=null && !turn.Human) Check(response?.Contains("Puedo ayudarte con estas opciones",StringComparison.OrdinalIgnoreCase)!=true,"NoUnnecessaryMenu");
                Check(f.Orders.Values.All(o=>o.Status==OrderStatus.PendingReview),"OnlyPendingReview");
                Check(f.Wording.Invocations.Count==0,"NoPaidCourtesy");
            }
            catch {outcome="Failed";throw;}
            finally
            {
                watch.Stop();var state=await f.Context();
                output.WriteLine(JsonSerializer.Serialize(new {Metric="CommercialCertification",Case=caseId,Workspace=workspaceHash,Message=Hash(source),
                    DurationMs=watch.Elapsed.TotalMilliseconds,Route=f.AiCalls>ai ? "Semantic" : "Local",SemanticCalls=f.AiCalls-ai,
                    Understanding=f.Understood.Skip(prepared).LastOrDefault()?.Interpretation.Intent.ToString() ?? "RecoveryOrShortcut",
                    Phase=state.CurrentStep?.Split(':')[0],FirestoreReads=measuredReads,FirestoreCommits=measuredCommits,
                    Calls=mocks.SelectMany((m,i)=>m.Invocations.Skip(offsets[i])).GroupBy(v=>v.Method.Name).ToDictionary(g=>g.Key,g=>g.Count()),
                    Delivery=f.Sent.GetValueOrDefault(source)?.Status.ToString() ?? "Suppressed",Orders=f.Orders.Count,Reservations=f.Bookings.Count,Outcome=outcome}));
            }
            f.InterpretationOverride=null;
        }
        Check(f.OrderWrites.Invocations.Count(i=>i.Method.Name=="CreateOrderAsync")==f.Orders.Count,"NoDuplicateOrderAttempt");
        Check(f.EngineReads.Invocations.Count(i=>i.Method.Name=="CreateReservationAsync")==f.Bookings.Count,"NoDuplicateReservationAttempt");
        Check(f.OfferingReads.Invocations.All(i=>Equals(i.Arguments[0],f.Workspace)),"WorkspaceIsolation");
        Check(f.Orders.Values.All(o=>o.Items.All(i=>f.Products.Any(p=>p.Id==i.ProductId && p.PriceMinorUnits==i.UnitPriceMinorUnits && p.Currency==i.Currency))),"PersistedCatalogFacts");
        void Check(bool condition,string criterion)=>Assert.True(condition,$"{caseId}: {criterion}");
    }
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    public sealed record CertificationConversation(string Id,string Sector,string Purpose,string[] Products,string[] Services,CertificationTurn[] Turns,bool AmbiguousLocations=false);
    public sealed class CertificationTurn
    {
        public string Text {get;set;}="";
        public string? Source {get;set;}
        public string? Action {get;set;}
        public string? SemanticIntent {get;set;}
        public string? Intent {get;set;}
        public bool NoKnowledgeRead {get;set;}
        public string[] Contains {get;set;}=[];
        public string[] NotContains {get;set;}=[];
        public bool Preserve {get;set;}
        public bool EmptyDraft {get;set;}
        public Dictionary<string,int>? Quantities {get;set;}
        public string? Goal {get;set;}
        public bool CheckGoal {get;set;}
        public string? Step {get;set;}
        public string? Date {get;set;}
        public string? Location {get;set;}
        public bool LocationMissing {get;set;}
        public bool TimeMissing {get;set;}
        public int? Orders {get;set;}
        public int? Bookings {get;set;}
        public bool Human {get;set;}
        public bool Silent {get;set;}
        public bool ProviderFailure {get;set;}
    }
}
