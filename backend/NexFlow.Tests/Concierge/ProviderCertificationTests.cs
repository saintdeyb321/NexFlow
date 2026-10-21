using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Engines.AI;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

public sealed class AuthorizedProviderFactAttribute : FactAttribute
{
    public AuthorizedProviderFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("NEXFLOW_CONCIERGE_REAL_AI_AUTHORIZED")!="1")
            Skip="NOT EXECUTED: requires explicit paid-call authorization and synthetic test credentials.";
    }
}

// Never runs in the local/RC3 integration suite by default; no appsettings or real conversations.
[Trait("Category","ProviderIntegration")]
public sealed class ProviderCertificationTests(ITestOutputHelper output)
{
    [AuthorizedProviderFact] public Task Groq_synthetic_interpretation_contract()=>Evaluate("Groq");
    [AuthorizedProviderFact] public Task Gemini_synthetic_interpretation_contract()=>Evaluate("Gemini");
    private async Task Evaluate(string name)
    {
        var key=Environment.GetEnvironmentVariable("NEXFLOW_TEST_"+name.ToUpperInvariant()+"_API_KEY");
        var model=Environment.GetEnvironmentVariable("NEXFLOW_TEST_"+name.ToUpperInvariant()+"_MODEL");
        Assert.True(!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(model),"Synthetic provider credentials/model required; no application credentials fallback.");
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{name+":ApiKey",key},{name+":Model",model},{name+":TimeoutSeconds","8"}}).Build();
        using var observer=new UsageObserver();using var client=new HttpClient(observer);
        IAiProvider provider=name=="Groq" ? new GroqAiProvider(client,config,NullLogger<GroqAiProvider>.Instance) : new GeminiAiProvider(client,config,NullLogger<GeminiAiProvider>.Instance);
        var cases=new[]{("P1","¿A cuánto está el cuaderno rayado?",ConversationIntent.ProductQuery),
            ("P2","Quisiera reservar evaluación general en Miraflores el 22 de octubre a las 16:00",ConversationIntent.Reservation),
            ("P3","Necesito conversar con una persona",ConversationIntent.Support),
            ("P4","¿Dónde queda el local?",ConversationIntent.Location),
            ("P5","¿A qué hora abren?",ConversationIntent.BusinessHours),
            ("P6","¿Cuánto dura la evaluación general?",ConversationIntent.ServiceQuery),
            ("P7","Quisiera dos cuadernos y un lapicero",ConversationIntent.Order),
            ("P8","No sé qué opción elegir",ConversationIntent.General)};
        var ws=Guid.NewGuid();var profile=new Mock<IBusinessProfileRepository>();profile.Setup(p=>p.GetProfileAsync(ws,It.IsAny<CancellationToken>())).ReturnsAsync(new BusinessProfileDto("Synthetic","","","","","America/Lima"));
        var clock=new Mock<IClock>();clock.SetupGet(c=>c.UtcNow).Returns(new DateTime(2026,10,20,21,0,0,DateTimeKind.Utc));
        var failed=0;
        foreach(var (id,text,expected) in cases)
        {
            var router=new Mock<IAiRouter>();string? raw=null;
            router.Setup(r=>r.ExecuteTaskAsync(AiTaskType.IntentExtraction,It.IsAny<string>(),It.IsAny<string>(),true,It.IsAny<CancellationToken>()))
                .Returns(async(AiTaskType _,string system,string input,bool json,CancellationToken ct)=>raw=await provider.GenerateTextAsync(system,input,json,ct));
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));var watch=Stopwatch.StartNew();var calls=observer.Calls;var tokens=observer.Tokens;var usage=observer.TokenResponses;
            AiInterpretation interpretation;
            try { interpretation=await new AiInterpreter(router.Object,clock.Object,profile.Object,NullLogger<AiInterpreter>.Instance)
                .InterpretAsync(ws,text,"",null,["CATALOG","SERVICES","ORDERS","RESERVATIONS","LOCATIONS","BUSINESS_HOURS"],deadline.Token); }
            catch(OperationCanceledException) when(deadline.IsCancellationRequested) { interpretation=new(){Intent=ConversationIntent.ProviderUnavailable,Failure=AiFailureKind.Timeout}; }
            var valid=false;try {using var parsed=JsonDocument.Parse(raw??"");valid=parsed.RootElement.ValueKind==JsonValueKind.Object;}catch(JsonException){}
            var entities=id!="P2" || interpretation.Date=="2026-10-22" && interpretation.Time=="16:00" && interpretation.Location?.Contains("Miraflores",StringComparison.OrdinalIgnoreCase)==true;
            var ok=valid && interpretation.Intent==expected && entities;if(!ok) failed++;
            output.WriteLine(JsonSerializer.Serialize(new {Metric="RealProvider",Provider=name,Case=id,DurationMs=watch.Elapsed.TotalMilliseconds,JsonValid=valid,IntentCorrect=interpretation.Intent==expected,EntitiesCorrect=entities,
                Failure=interpretation.Failure.ToString(),Requests=observer.Calls-calls,ReportedTokens=observer.Tokens-tokens,TokenUsageAvailable=observer.TokenResponses>usage,HttpStatus=observer.LastStatus,
                Fallback="NotInvoked_DirectProviderContract",CompositionCalls=0,Cost="NO_MEDIDO_NoVerifiedTariff"}));
        }
        Assert.True(failed==0,$"Provider contract failed in {failed} synthetic cases; inspect metadata, never print response text.");
    }
    private sealed class UsageObserver() : DelegatingHandler(new HttpClientHandler())
    {
        public int Calls {get;private set;} public long Tokens {get;private set;} public int TokenResponses {get;private set;} public int LastStatus {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;LastStatus=0;var response=await base.SendAsync(request,ct);LastStatus=(int)response.StatusCode;
            var data=await response.Content.ReadAsStringAsync(ct);
            try {using var doc=JsonDocument.Parse(data);
                if(doc.RootElement.TryGetProperty("usage",out var u) && u.TryGetProperty("total_tokens",out var total)) { Tokens+=total.GetInt64();TokenResponses++; }
                if(doc.RootElement.TryGetProperty("usageMetadata",out var g) && g.TryGetProperty("totalTokenCount",out var gt)) { Tokens+=gt.GetInt64();TokenResponses++; }
            }catch(JsonException){}
            return response;
        }
    }
}
