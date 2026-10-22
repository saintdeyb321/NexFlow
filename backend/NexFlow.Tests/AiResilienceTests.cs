using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Engines.AI;
using Xunit;

namespace NexFlow.Tests;

public sealed class AiResilienceTests
{
    [Theory]
    [InlineData("Groq")]
    [InlineData("Gemini")]
    public async Task Successful_provider_uses_configured_model_json_mode_and_a_bounded_output_budget(string provider)
    {
        var response = provider == "Groq" ? """{"choices":[{"message":{"content":"{\"Intent\":\"General\"}"}}]}""" : """{"candidates":[{"content":{"parts":[{"text":"{\"Intent\":\"General\"}"}]}}]}""";
        using var transport = new Transport(200, response); using var http = new HttpClient(transport);
        var config = Config(); if (provider == "Groq") config["Groq:Model"] = "openai/gpt-oss-20b";
        Assert.Contains("General", await Provider(provider, http, config).GenerateTextAsync("system", "user", true));
        using var body = System.Text.Json.JsonDocument.Parse(transport.Body!);
        if (provider == "Groq")
        {
            Assert.Equal("openai/gpt-oss-20b", body.RootElement.GetProperty("model").GetString());
            Assert.Equal("low", body.RootElement.GetProperty("reasoning_effort").GetString());
            Assert.Equal(1024, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
            Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        }
        else
        {
            Assert.Contains("explicit-gemini-model:generateContent", transport.Url);
            Assert.Equal(1024, body.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
            Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
        }
    }
    [Theory]
    [InlineData("Groq", 400)] [InlineData("Groq", 401)] [InlineData("Groq", 403)] [InlineData("Groq", 404)]
    [InlineData("Groq", 429)] [InlineData("Groq", 500)] [InlineData("Groq", 503)]
    [InlineData("Gemini", 400)] [InlineData("Gemini", 401)] [InlineData("Gemini", 403)] [InlineData("Gemini", 404)]
    [InlineData("Gemini", 429)] [InlineData("Gemini", 500)] [InlineData("Gemini", 503)]
    public async Task Real_provider_preserves_http_status_and_never_logs_response_bodies_or_secrets(string provider, int status)
    {
        using var transport = new Transport(status, "sensitive-customer-data test-only-secret"); using var http = new HttpClient(transport);
        var logs = new List<string>(); var model = Provider(provider, http, Config(), logs);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => model.GenerateTextAsync("system", "user", true));
        Assert.Equal((HttpStatusCode)status, ex.StatusCode); Assert.Equal(1, transport.Calls);
        Assert.DoesNotContain("test-only-secret", transport.Url!); Assert.DoesNotContain("test-only-secret", string.Join("\n", logs));
        Assert.DoesNotContain("sensitive-customer-data", string.Join("\n", logs));
        if (provider == "Gemini") Assert.True(transport.HasGeminiKeyHeader);
    }

    [Theory]
    [InlineData("Groq", "not-json")]
    [InlineData("Groq", "{}")]
    [InlineData("Groq", "{\"choices\":[{\"message\":{\"content\":12}}]}")]
    [InlineData("Gemini", "not-json")]
    [InlineData("Gemini", "{\"candidates\":[]}")]
    [InlineData("Gemini", "{\"candidates\":[null]}")]
    public async Task Empty_blocked_or_invalid_provider_json_is_not_a_successful_interpretation(string provider, string response)
    {
        using var transport = new Transport(200, response); using var http = new HttpClient(transport);
        await Assert.ThrowsAsync<AiInvalidResponseException>(() => Provider(provider, http, Config()).GenerateTextAsync("system", "user", true));
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData("Groq")]
    [InlineData("Gemini")]
    public async Task A_missing_model_is_not_replaced_with_a_fictional_default_or_sent_to_the_provider(string provider)
    {
        using var transport = new Transport(200, "{}"); using var http = new HttpClient(transport);
        var config = Config(); config[$"{provider}:Model"] = null;
        await Assert.ThrowsAsync<AiProviderConfigurationException>(() => Provider(provider, http, config).GenerateTextAsync("system", "user"));
        Assert.Equal(0, transport.Calls);
    }

    [Theory]
    [InlineData(400, false)] [InlineData(401, false)] [InlineData(403, false)] [InlineData(404, false)]
    [InlineData(408, true)] [InlineData(429, true)] [InlineData(500, true)] [InlineData(503, true)] [InlineData(0, true)]
    public async Task Router_uses_exactly_one_cross_provider_fallback_only_for_transient_errors(int status, bool fallback)
    {
        var primary = new Mock<IAiProvider>(MockBehavior.Strict); primary.SetupGet(p => p.ProviderType).Returns(AiProviderType.Groq);
        primary.Setup(p => p.GenerateTextAsync("system", "user", true, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("Sensitive URL must not be logged", null, status == 0 ? null : (HttpStatusCode)status));
        var secondary = new Mock<IAiProvider>(MockBehavior.Strict); secondary.SetupGet(p => p.ProviderType).Returns(AiProviderType.Gemini);
        secondary.Setup(p => p.GenerateTextAsync("system", "user", true, It.IsAny<CancellationToken>())).ReturnsAsync("{\"Intent\":\"General\"}");
        var logs = new List<string>();
        var router = new AiRouter(new[] { primary.Object, secondary.Object }, new CaptureLogger<AiRouter>(logs));
        if (fallback) Assert.Contains("General", await router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "system", "user", true, default));
        else await Assert.ThrowsAsync<HttpRequestException>(() => router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "system", "user", true, default));
        secondary.Verify(p => p.GenerateTextAsync("system", "user", true, It.IsAny<CancellationToken>()), fallback ? Times.Once() : Times.Never());
        var phases = logs.Where(l => l.StartsWith("ConversationPhase", StringComparison.Ordinal)).ToArray();
        Assert.Equal(fallback ? 2 : 1, phases.Length);
        Assert.All(phases, l => { Assert.Contains("Phase=AiProvider", l); Assert.Contains("AiInvocations=1", l); });
        Assert.Contains("Outcome=Error", phases[0]);
        if (fallback) { Assert.Contains("Provider=Gemini", phases[1]); Assert.Contains("Outcome=Success", phases[1]); }
        Assert.DoesNotContain(logs, l => l.Contains("Sensitive URL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Caller_cancellation_does_not_spend_a_fallback_and_two_unavailable_providers_have_a_bounded_failure()
    {
        var primary = new Mock<IAiProvider>(); primary.SetupGet(p => p.ProviderType).Returns(AiProviderType.Groq);
        var secondary = new Mock<IAiProvider>(); secondary.SetupGet(p => p.ProviderType).Returns(AiProviderType.Gemini);
        primary.Setup(p => p.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException());
        secondary.Setup(p => p.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("Down", null, HttpStatusCode.ServiceUnavailable));
        var router = new AiRouter(new[] { primary.Object, secondary.Object }, NullLogger<AiRouter>.Instance);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "s", "u", true, stop.Token));
        secondary.Verify(p => p.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Never());
        await Assert.ThrowsAsync<HttpRequestException>(() => router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "s", "u", true, default));
        secondary.Verify(p => p.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Act\":\"Confirmation\",\"Evidence\":[{\"Slot\":\"Act\",\"Text\":\"consulta\",\"NormalizedValue\":\"Confirmation\"}]}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Act\":\"StartOperation\"}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Evidence\":null}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Evidence\":[{\"Slot\":\"WorkspaceId\",\"Text\":\"consulta\",\"NormalizedValue\":\"foreign\"}]}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Evidence\":[{\"Slot\":\"Date\",\"Text\":\"texto inventado\",\"NormalizedValue\":\"2026-10-22\"}]}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Evidence\":[{\"Slot\":\"Date\",\"Text\":\"consulta\",\"NormalizedValue\":\"2026-10-22\"},{\"Slot\":\"Date\",\"Text\":\"consulta\",\"NormalizedValue\":\"2026-10-23\"}]}")]
    [InlineData("{\"Intent\":\"Order\",\"RequiresClarification\":\"false\"}")]
    [InlineData("{\"Intent\":\"Order\",\"Confirmed\":true}")]
    [InlineData("{\"Intent\":\"Reservation\",\"BookingProposal\":{\"WorkspaceId\":\"foreign\"}}")]
    [InlineData("{\"Intent\":\"Request\",\"DeniedResponse\":null}")]
    [InlineData("{\"Intent\":1}")]
    [InlineData("{\"Intent\":\"ProviderUnavailable\"}")]
    [InlineData("{\"Intent\":\"Order\",\"WorkspaceId\":\"foreign\"}")]
    [InlineData("{\"Intent\":\"General\",\"intent\":\"Order\"}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Time\":\"25:00\"}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Date\":\"mañana\"}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Directive\":\"CancelCurrentFlow\"}")]
    [InlineData("{\"Intent\":\"Reservation\",\"CustomerName\":\"Nombre inventado\"}")]
    [InlineData("{\"Intent\":\"Reservation\",\"Directive\":\"ChangeLocation\"}")]
    public async Task Strict_interpretation_rejects_untrusted_schema_entities_directives_and_server_intents(string json)
    {
        var router = new Mock<IAiRouter>(); router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), "consulta", true, It.IsAny<CancellationToken>())).ReturnsAsync(json);
        var interpretation = await Interpreter(router.Object).InterpretAsync(Guid.NewGuid(), "consulta", "RESERVATION", "COLLECT_TIME", new() { "RESERVATIONS", "SERVICES" }, default);
        Assert.Equal(ConversationIntent.ProviderUnavailable, interpretation.Intent); Assert.Equal(AiFailureKind.InvalidResponse, interpretation.Failure);
        Assert.Null(interpretation.CustomerName); Assert.Null(interpretation.Time); Assert.Equal(ConversationDirective.None, interpretation.Directive);
    }

    [Theory]
    [InlineData(400, AiFailureKind.Rejected)] [InlineData(401, AiFailureKind.Configuration)] [InlineData(403, AiFailureKind.Configuration)]
    [InlineData(404, AiFailureKind.Configuration)] [InlineData(429, AiFailureKind.RateLimit)] [InlineData(503, AiFailureKind.Unavailable)]
    public async Task Interpreter_differentiates_configuration_rejection_saturation_and_unavailability(int status, AiFailureKind expected)
    {
        var router = new Mock<IAiRouter>(); router.Setup(r => r.ExecuteTaskAsync(It.IsAny<AiTaskType>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("sanitized", null, (HttpStatusCode)status));
        var result = await Interpreter(router.Object).InterpretAsync(Guid.NewGuid(), "consulta", "", null, new(), default);
        Assert.Equal(expected, result.Failure); Assert.Equal(status == 429 ? ConversationIntent.RateLimited : ConversationIntent.ProviderUnavailable, result.Intent);
    }

    [Theory]
    [InlineData("Groq")]
    [InlineData("Gemini")]
    public async Task Real_http_deadline_is_bounded_and_the_request_is_not_retried(string provider)
    {
        using var transport = new Transport(0, ""); using var http = new HttpClient(transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(provider, http, Config()).GenerateTextAsync("system", "user", true));
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task A_healthy_primary_does_not_require_an_unused_fallback_registration()
    {
        var primary = new Mock<IAiProvider>(); primary.SetupGet(p => p.ProviderType).Returns(AiProviderType.Groq);
        primary.Setup(p => p.GenerateTextAsync("system", "user", true, It.IsAny<CancellationToken>())).ReturnsAsync("{\"Intent\":\"General\"}");
        var router = new AiRouter(new[] { primary.Object }, NullLogger<AiRouter>.Instance);
        Assert.Equal("{\"Intent\":\"General\"}", await router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "system", "user", true, default));
    }

    private static AiInterpreter Interpreter(IAiRouter router)
    {
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(DateTime.UtcNow);
        return new(router, clock.Object, Mock.Of<IBusinessProfileRepository>(), NullLogger<AiInterpreter>.Instance);
    }
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Groq:ApiKey"] = "test-only-secret", ["Gemini:ApiKey"] = "test-only-secret", ["Groq:Model"] = "explicit-groq-model", ["Gemini:Model"] = "explicit-gemini-model", ["Groq:TimeoutSeconds"] = "1", ["Gemini:TimeoutSeconds"] = "1" }).Build();
    private static IAiProvider Provider(string provider, HttpClient http, IConfiguration config, List<string>? logs = null)
        => provider == "Groq" ? new GroqAiProvider(http, config, new CaptureLogger<GroqAiProvider>(logs)) : new GeminiAiProvider(http, config, new CaptureLogger<GeminiAiProvider>(logs));
    private sealed class CaptureLogger<T>(List<string>? entries) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> format) => entries?.Add(format(state, exception) + exception);
    }
    private sealed class Transport(int status, string response) : HttpMessageHandler
    {
        public int Calls { get; private set; } public string? Url { get; private set; } public bool HasGeminiKeyHeader { get; private set; } public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Url = request.RequestUri!.ToString(); HasGeminiKeyHeader = request.Headers.Contains("x-goog-api-key");
            Body = await request.Content!.ReadAsStringAsync(ct);
            if (status == 0) await Task.Delay(Timeout.Infinite, ct);
            return new((HttpStatusCode)status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
