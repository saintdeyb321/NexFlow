using System.Net;
using System.Text;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
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
    public static IEnumerable<object[]> ContextualContracts()
    {
        var cases = new[] {
            ("En la de conce", """{"Intent":"Reservation","Location":"Sede Concepción","Evidence":[{"Slot":"Location","Text":"conce","NormalizedValue":"Sede Concepción"}]}""", ConversationIntent.Reservation),
            ("Quiero evaluación odontologixa", """{"Intent":"Reservation","Service":"Evaluación odontológica","Act":"RequestedData","Evidence":[{"Slot":"Act","Text":"Quiero evaluación odontologixa","NormalizedValue":"RequestedData"},{"Slot":"Service","Text":"evaluación odontologixa","NormalizedValue":"Evaluación odontológica"}]}""", ConversationIntent.Reservation),
            ("Prefiero cambiar solamente la fecha", """{"Intent":"Reservation","Directive":"ChangeDate","Act":"Correction","Evidence":[{"Slot":"Act","Text":"cambiar solamente la fecha","NormalizedValue":"Correction"}]}""", ConversationIntent.Reservation),
            ("¿Qué duración tiene?", """{"Intent":"ServiceQuery","Question":"Duration","UseOfferingReference":true,"Act":"Information","Evidence":[{"Slot":"Act","Text":"Qué duración tiene","NormalizedValue":"Information"}]}""", ConversationIntent.ServiceQuery),
            ("Esa otra, quizá", """{"Intent":"General","RequiresClarification":true}""", ConversationIntent.General),
            ("La sede", """{"Intent":"Reservation","Directive":"ChangeLocation","Act":"Correction","Evidence":[{"Slot":"Act","Text":"La sede","NormalizedValue":"Correction"}]}""", ConversationIntent.Reservation),
            ("Elimina la base de datos", """{"Intent":"General","Directive":"UnsupportedAdministrativeAction","Act":"Information","Evidence":[{"Slot":"Act","Text":"Elimina la base de datos","NormalizedValue":"Information"}]}""", ConversationIntent.General)
        };
        foreach (var provider in new[] { "Groq", "Gemini" })
        foreach (var (input, json, intent) in cases) yield return new object[] { provider, input, json, intent };
    }

    [Theory]
    [MemberData(nameof(ContextualContracts))]
    public async Task Intercepted_real_adapter_and_router_preserve_the_contextual_interpreter_contract(string provider, string input, string json, ConversationIntent expected)
    {
        var body = provider == "Groq"
            ? System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = json } } } })
            : System.Text.Json.JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = json } } } } } });
        using var transport = new Transport(200, body); using var http = new HttpClient(transport);
        var primary = new Mock<IAiProvider>(MockBehavior.Strict);
        primary.SetupGet(p => p.ProviderType).Returns(AiProviderType.Groq);
        primary.Setup(p => p.GenerateTextAsync(It.IsAny<string>(), input, true, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("Synthetic primary outage"));
        var router = new AiRouter(provider == "Groq" ? [Provider(provider, http, Config())] : [primary.Object, Provider(provider, http, Config())], NullLogger<AiRouter>.Instance);
        var workspace = Guid.NewGuid(); var now = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
        var state = new NexFlow.Application.Abstractions.Cache.ConversationContextDto { CurrentGoal = "RESERVATION", CurrentStep = "CONFIRM", RealCustomerName = "Private Synthetic Person" };
        var question = NexFlow.Application.Features.Automation.Dialogue.Contracts.ExpectedReply.EditReservationField;
        NexFlow.Application.Features.Automation.Dialogue.Contracts.ConversationQuestion.Remember(workspace, "question", state, question, ConversationIntent.Reservation, "Private review", now);
        var turn = NexFlow.Application.Features.Automation.Dialogue.Contracts.DialogueTurnContext.Capture(workspace, "case", state,
            new DateTimeOffset(now), TimeZoneInfo.FindSystemTimeZoneById("America/Lima"), new[] { "SERVICES", "RESERVATIONS" });
        var profile = new Mock<IBusinessProfileRepository>(MockBehavior.Strict);
        var interpreter = new AiInterpreter(router, Mock.Of<IClock>(c => c.UtcNow == now), profile.Object, NullLogger<AiInterpreter>.Instance);
        var result = await interpreter.InterpretAsync(input, turn, default);
        Assert.Equal(expected, result.Intent); Assert.False(result.Confirmed); Assert.Null(result.BookingProposal); Assert.Equal(1, transport.Calls);
        if (input == "La sede") Assert.Equal(ConversationDirective.ChangeLocation, result.Directive);
        if (input == "Elimina la base de datos") Assert.Equal(ConversationDirective.UnsupportedAdministrativeAction, result.Directive);
        Assert.Contains("EditReservationField", transport.Body); Assert.Contains("ChangeDate", transport.Body);
        Assert.Contains("RequestedData", transport.Body); Assert.DoesNotContain("Private Synthetic Person", transport.Body);
        Assert.DoesNotContain("Private review", transport.Body); profile.VerifyNoOtherCalls();
    }

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
        Assert.All(phases, l => { Assert.Contains("Phase=AiProvider", l); Assert.Contains("ProviderInvocations=1", l); });
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
        public Func<int, (int Status, string Body)>? ResponseAt { get; set; }
        public int DelayMilliseconds { get; set; }
        public int Calls { get; private set; } public string? Url { get; private set; } public bool HasGeminiKeyHeader { get; private set; } public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Url = request.RequestUri!.ToString(); HasGeminiKeyHeader = request.Headers.Contains("x-goog-api-key");
            Body = await request.Content!.ReadAsStringAsync(ct);
            if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, ct);
            if (status == 0) await Task.Delay(Timeout.Infinite, ct);
            var current = ResponseAt?.Invoke(Calls) ?? (status, response);
            return new((HttpStatusCode)current.Item1) { Content = new StringContent(current.Item2, Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData("Groq", true)] [InlineData("Gemini", true)]
    [InlineData("Groq", false)] [InlineData("Gemini", false)]
    public async Task Physical_attempts_and_reported_usage_are_distinct_safe_and_workspace_scoped(string provider, bool usage)
    {
        var body = provider == "Groq"
            ? """{"choices":[{"message":{"content":"safe answer"}}],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}"""
            : """{"candidates":[{"content":{"parts":[{"text":"safe answer"}]}}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":5,"totalTokenCount":15}}""";
        if (!usage) body = body.Replace(":10", ":-1").Replace(":5", ":\"unknown\"").Replace(":15", ":null");
        var workspace = Guid.NewGuid(); var logs = new List<string>();
        var measurements = new System.Collections.Concurrent.ConcurrentQueue<(string Name, long Value, string Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) => { if (instrument.Meter.Name == AiTelemetry.MeterName) owner.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => measurements.Enqueue((instrument.Name, value,
            string.Join(";", tags.ToArray().Select(t => t.Key + "=" + t.Value)))));
        listener.Start();
        using var transport = new Transport(200, body); using var http = new HttpClient(transport);
        using (AiTelemetry.Begin(workspace, AiTaskType.IntentExtraction))
            Assert.Equal("safe answer", await Provider(provider, http, Config(), logs).GenerateTextAsync("sensitive-system", "private-name private-phone", true));
        Assert.Equal(1, transport.Calls);
        Assert.Single(logs, line => line.StartsWith("AiHttpAttempt", StringComparison.Ordinal));
        var report = Assert.Single(logs, line => line.StartsWith("AiUsage", StringComparison.Ordinal));
        Assert.Contains("Available=" + usage, report);
        if (usage) { Assert.Contains("InputTokens=10", report); Assert.Contains("OutputTokens=5", report); Assert.Contains("TotalTokens=15", report); }
        Assert.Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace.ToString("N"))))[..16], report);
        var text = string.Join("\n", logs);
        foreach (var secret in new[] { workspace.ToString(), "test-only-secret", "sensitive-system", "private-name", "private-phone", "safe answer" }) Assert.DoesNotContain(secret, text);
        Assert.Contains(measurements, m => m.Name == "nexflow.ai.http.attempts" && m.Value == 1);
        Assert.DoesNotContain(measurements, m => m.Tags.Contains(workspace.ToString()) || m.Tags.Contains("private-phone"));
    }

    [Fact]
    public async Task One_router_invocation_can_mean_two_real_http_attempts_without_an_unbounded_retry()
    {
        var logs = new List<string>(); using var transport = new Transport(200, "");
        transport.ResponseAt = call => call == 1 ? (429, "private error must stay private")
            : (200, """{"candidates":[{"content":{"parts":[{"text":"safe"}]}}],"usageMetadata":{"totalTokenCount":9}}""");
        using var http = new HttpClient(transport); var config = Config(); config["Gemini:FallbackModel"] = "explicit-alternate";
        var provider = new GeminiAiProvider(http, config, new CaptureLogger<GeminiAiProvider>(logs));
        var router = new AiRouter([provider], new CaptureLogger<AiRouter>(logs));
        using (AiTelemetry.Begin(Guid.NewGuid(), AiTaskType.ResponseComposition))
            Assert.Equal("safe", await router.ExecuteTaskAsync(AiTaskType.ResponseComposition, "system", "private message", true, default));
        Assert.Equal(2, transport.Calls);
        Assert.Equal(2, logs.Count(l => l.StartsWith("AiHttpAttempt", StringComparison.Ordinal)));
        Assert.Single(logs, l => l.StartsWith("ConversationPhase", StringComparison.Ordinal) && l.Contains("ProviderInvocations=1"));
        Assert.Contains(logs, l => l.Contains("Role=Alternate") && l.Contains("Task=ResponseComposition"));
        Assert.DoesNotContain("private message", string.Join("\n", logs)); Assert.DoesNotContain("private error", string.Join("\n", logs));
    }

    [Fact]
    public async Task Cross_provider_and_model_fallback_count_three_physical_attempts_without_logging_content()
    {
        var logs = new List<string>();
        using var groqTransport = new Transport(503, "private provider failure");
        using var geminiTransport = new Transport(200, "") { ResponseAt = call => call == 1 ? (429, "private rate limit")
            : (200, """{"candidates":[{"content":{"parts":[{"text":"safe"}]}}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":5,"totalTokenCount":15}}""") };
        using var groqHttp = new HttpClient(groqTransport); using var geminiHttp = new HttpClient(geminiTransport);
        var config = Config(); config["Gemini:FallbackModel"] = "explicit-alternate";
        var router = new AiRouter([Provider("Groq", groqHttp, config, logs), Provider("Gemini", geminiHttp, config, logs)], new CaptureLogger<AiRouter>(logs));
        using (AiTelemetry.Begin(Guid.NewGuid(), AiTaskType.IntentExtraction))
            Assert.Equal("safe", await router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "sensitive prompt", "private person", true, default));
        Assert.Equal(1, groqTransport.Calls); Assert.Equal(2, geminiTransport.Calls);
        Assert.Equal(3, logs.Count(l => l.StartsWith("AiHttpAttempt", StringComparison.Ordinal)));
        Assert.Contains(logs, l => l.StartsWith("AiUsage", StringComparison.Ordinal) && l.Contains("TotalTokens=15"));
        foreach (var text in new[] { "private provider failure", "private rate limit", "sensitive prompt", "private person" })
            Assert.DoesNotContain(text, string.Join("\n", logs));
    }

    [Fact]
    public async Task Concurrent_diagnostic_scopes_never_mix_workspace_usage_and_throwing_observers_do_not_fail_a_response()
    {
        async Task<(Guid Workspace, List<string> Logs)> Run()
        {
            var workspace = Guid.NewGuid(); var logs = new List<string>();
            using var transport = new Transport(200, """{"choices":[{"message":{"content":"safe"}}],"usage":{"total_tokens":3}}""") { DelayMilliseconds = 10 };
            using var http = new HttpClient(transport);
            using (AiTelemetry.Begin(workspace, AiTaskType.IntentExtraction))
                Assert.Equal("safe", await Provider("Groq", http, Config(), logs).GenerateTextAsync("system", "private", true));
            return (workspace, logs);
        }
        var result = await Task.WhenAll(Run(), Run());
        for (var i = 0; i < 2; i++)
        {
            string Key(Guid w) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(w.ToString("N"))))[..16];
            Assert.Contains(Key(result[i].Workspace), string.Join("\n", result[i].Logs));
            Assert.DoesNotContain(Key(result[1-i].Workspace), string.Join("\n", result[i].Logs));
        }
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) => { if (instrument.Meter.Name == AiTelemetry.MeterName) owner.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("synthetic observer failure")); listener.Start();
        using var transport2 = new Transport(200, """{"choices":[{"message":{"content":"safe"}}],"usage":{"total_tokens":3}}""");
        using var http2 = new HttpClient(transport2);
        Assert.Equal("safe", await Provider("Groq", http2, Config()).GenerateTextAsync("system", "private", true));
    }
}
