using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Engines.AI;
using Xunit;

namespace NexFlow.Tests.Concierge;

[CollectionDefinition("CTA probe telemetry", DisableParallelization = true)]
public sealed class CtaProbeCollection;

[Collection("CTA probe telemetry")]
public sealed class CtaProbeSafetyTests
{
    [Fact]
    public async Task Http_budget_is_shared_including_the_real_router_fallback_and_never_refunds_failures()
    {
        var calls = 0; var wire = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        wire.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        wire.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage request, CancellationToken _) => {
                calls++;
                return Task.FromResult(request.RequestUri!.Host == "api.groq.com" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"candidates":[{"content":{"parts":[{"text":"{\"Intent\":\"General\",\"RequiresClarification\":true}"}]}}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":4,"totalTokenCount":14}}""") });
            });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Groq:ApiKey"] = "synthetic-not-a-key", ["Groq:Model"] = "synthetic", ["Gemini:ApiKey"] = "synthetic-not-a-key", ["Gemini:Model"] = "synthetic" }).Build();
        var budget = new CtaProbeBudget();
        using var http = new HttpClient(new CtaProbeTransport(budget, wire.Object));
        var groq = new GroqAiProvider(http, settings, NullLogger<GroqAiProvider>.Instance);
        var gemini = new GeminiAiProvider(http, settings, NullLogger<GeminiAiProvider>.Instance);
        var router = new AiRouter([groq, gemini], NullLogger<AiRouter>.Instance);
        using var metrics = new CtaProbeMeasurements();
        for (var i = 0; i < 6; i++) Assert.Equal("{\"Intent\":\"General\",\"RequiresClarification\":true}",
            await router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "synthetic", "synthetic", true, default));
        Assert.Equal(12, budget.Attempts); Assert.Equal(12, calls); Assert.Equal(12, budget.Observations.Count);
        Assert.Equal(60, metrics.Snapshot()!["Gemini:Input"]); Assert.Equal(24, metrics.Snapshot()!["Gemini:Output"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "synthetic", "synthetic", true, default));
        Assert.Equal(12, calls); Assert.Equal(12, budget.Attempts);
    }

    [Fact]
    public async Task Probe_rejects_other_hosts_and_secret_query_strings_before_using_the_network()
    {
        var wire = new Mock<HttpMessageHandler>(MockBehavior.Strict); wire.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        var budget = new CtaProbeBudget(); using var http = new HttpClient(new CtaProbeTransport(budget, wire.Object));
        await Assert.ThrowsAsync<InvalidOperationException>(() => http.PostAsync("https://example.invalid", new StringContent("")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => http.PostAsync("https://api.groq.com/?key=synthetic", new StringContent("")));
        Assert.Equal(0, budget.Attempts); Assert.Empty(budget.Observations);
    }

    [Fact]
    public void The_twelve_case_manifest_has_no_confirmation_authority_and_output_excludes_arbitrary_model_text()
    {
        var cases = CtaProbeCases.All().ToArray(); Assert.Equal(12, cases.Length); Assert.Equal(12, cases.Select(c => c.Id).Distinct().Count());
        Assert.All(cases, c => Assert.True(c.Context.SupportsFocusSwitch));
        Assert.Null(cases.Single(c => c.Id == "C12").Context.Question);
        var synthetic = new AiInterpretation { Intent = ConversationIntent.Order, Items = [new("untrusted label", "untrusted text", OrderEditKind.Set, 1)],
            Evidence = [new("Act", "untrusted text", "untrusted value")] };
        var safe = JsonSerializer.Serialize(CtaProbeCases.Safe(synthetic));
        Assert.DoesNotContain("untrusted", safe); Assert.Contains("outside synthetic catalog", safe);
        Assert.Contains("ModelConsentAccepted", safe); Assert.False(synthetic.Confirmed);
    }
}
