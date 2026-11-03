using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Infrastructure.Engines.AI;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentEvaluationBudgetTests
{
    [Theory]
    [InlineData(1, 30_000, 1)]
    [InlineData(12, 2_048, 0)]
    public async Task HTTP_and_conservative_token_caps_cover_fallback_without_unaccounted_attempts(int maxHttp, long tokens, int expected)
    {
        var budget = new AgentEvaluationHttpBudget(maxHttp, tokens);
        var wire = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        wire.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        wire.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") });
        using var client = new HttpClient(budget.Handler(wire.Object));
        await Assert.ThrowsAsync<AiBudgetDeniedException>(async () => {
            using var a = await client.PostAsync("https://api.groq.com/test", new StringContent(new string('x', 1100)));
            using var b = await client.PostAsync("https://generativelanguage.googleapis.com/test", new StringContent(new string('x', 1100)));
        });
        Assert.Equal(expected, budget.Attempts); Assert.Null(budget.ReportedTokens);
        wire.Protected().Verify("SendAsync", Times.Exactly(expected), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Real_adapters_and_router_fallback_are_measured_with_intercepted_HTTP_not_real_NLU()
    {
        var budget = new AgentEvaluationHttpBudget(3, 30_000); var wire = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        wire.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        wire.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) => new HttpResponseMessage(
                request.RequestUri!.Host == "api.groq.com" ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK) {
                Content = new StringContent(request.RequestUri.Host == "api.groq.com" ? "{}"
                    : "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"verified\"}]}}],\"usageMetadata\":{\"totalTokenCount\":47}}") });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Groq:ApiKey"] = "synthetic", ["Groq:Model"] = "synthetic", ["Gemini:ApiKey"] = "synthetic", ["Gemini:Model"] = "synthetic" }).Build();
        using var gc = new HttpClient(budget.Handler(wire.Object)); using var mc = new HttpClient(budget.Handler(wire.Object));
        var router = new AiRouter([new GroqAiProvider(gc, config, NullLogger<GroqAiProvider>.Instance),
            new GeminiAiProvider(mc, config, NullLogger<GeminiAiProvider>.Instance)], NullLogger<AiRouter>.Instance);
        Assert.Equal("verified", await router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "contract", "synthetic", true, default));
        Assert.Equal(2, budget.Attempts); Assert.Equal(47, budget.ReportedTokens); Assert.Equal(1, budget.UsageResponses);
        Assert.InRange(budget.ReservedTokens, 2048, 30_000);
    }
}
