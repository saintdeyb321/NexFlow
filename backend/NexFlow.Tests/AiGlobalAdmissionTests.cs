using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Infrastructure.Engines.AI;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class AiGlobalAdmissionTests
{
    private sealed class Fixture
    {
        public InMemoryFirestore Store { get; } = new();
        public DateTime Now { get; set; } = new(2026, 11, 2, 14, 0, 0, DateTimeKind.Utc);
        public AiQuotaPool Pool { get; } = new() { Provider = AiProviderType.Groq, Key = "synthetic-account", Models = ["test-model"],
            ResetTimeZone = "UTC", RequestsPerMinute = 100, RequestsPerDay = 1000, TokensPerMinute = 1_000_000,
            TokensPerDay = 10_000_000, TokenReservationPerAttempt = 10_000, SafetyMargin = 0, WorkspaceShare = .5 };
        public AiQuotaOptions Options { get; } = new() { Enabled = true };
        public FirestoreAiRequestAdmission Admission()
        {
            var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => Now);
            return new(Store.Db, Microsoft.Extensions.Options.Options.Create(Options), clock.Object);
        }
        public Fixture() { Options.Pools.Add(Pool); }
        public async Task Admit(Guid workspace)
        {
            using var scope = AiTelemetry.Begin(workspace, AiTaskType.IntentExtraction);
            await Admission().AdmitAsync(AiProviderType.Groq, "test-model", 100, 1024, default);
        }
        public string Path => "ai-provider-budgets/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Groq:synthetic-account")));
    }

    [Theory]
    [InlineData("RPM")]
    [InlineData("RPD")]
    [InlineData("TPM")]
    [InlineData("TPD")]
    public async Task Account_limits_are_atomic_shared_durable_and_do_not_roll_back_windows(string limit)
    {
        var f = new Fixture();
        switch (limit) { case "RPM": f.Pool.RequestsPerMinute = 2; break; case "RPD": f.Pool.RequestsPerDay = 2; break;
            case "TPM": f.Pool.TokensPerMinute = 20_000; break; case "TPD": f.Pool.TokensPerDay = 20_000; break; }
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        f.Store.SynchronizeReads(f.Path);
        await Task.WhenAll(f.Admit(a), f.Admit(b));
        await Assert.ThrowsAsync<AiBudgetDeniedException>(() => f.Admit(Guid.NewGuid()));
        var old = f.Now;
        f.Now = limit is "RPD" or "TPD" ? f.Now.AddDays(1) : f.Now.AddMinutes(1);
        await f.Admit(a); f.Now = old;
        await Assert.ThrowsAsync<AiBudgetDeniedException>(() => f.Admit(b));
    }

    [Fact]
    public async Task One_workspace_cannot_spend_the_share_reserved_for_others_or_reset_it_by_recreating_the_service()
    {
        var f = new Fixture(); f.Pool.RequestsPerMinute = 4; var a = Guid.NewGuid();
        await f.Admit(a); await f.Admit(a);
        await Assert.ThrowsAsync<AiBudgetDeniedException>(() => f.Admit(a));
        await f.Admit(Guid.NewGuid()); await f.Admit(Guid.NewGuid());
        var json = (await f.Store.Db.Document(f.Path).GetSnapshotAsync()).GetValue<string>("budgetJson");
        Assert.DoesNotContain(a.ToString(), json); Assert.DoesNotContain("secret", json);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("workspace")]
    [InlineData("envelope")]
    [InlineData("cancel")]
    public async Task Unknown_quota_identity_unbounded_output_and_cancellation_cannot_authorize_a_request(string kind)
    {
        var f = new Fixture(); if (kind == "unknown") f.Options.Pools.Clear();
        using var scope = kind == "workspace" ? null : AiTelemetry.Begin(Guid.NewGuid(), AiTaskType.IntentExtraction);
        using var cts = new CancellationTokenSource(); if (kind == "cancel") cts.Cancel();
        var task = f.Admission().AdmitAsync(AiProviderType.Groq, "test-model", 100, kind == "envelope" ? null : 1024, cts.Token);
        if (kind == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        else await Assert.ThrowsAsync<AiBudgetDeniedException>(() => task);
        Assert.Equal(0, f.Store.CommitRpcCalls);
    }

    [Fact]
    public async Task Retry_after_is_durable_and_an_unbudgeted_fallback_never_calls_its_http_transport()
    {
        var f = new Fixture(); var workspace = Guid.NewGuid(); using var scope = AiTelemetry.Begin(workspace, AiTaskType.IntentExtraction);
        var groq = new Mock<HttpMessageHandler>(); var gemini = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        groq.Protected().SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(RateLimited()).ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}") });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Groq:ApiKey"] = "synthetic", ["Groq:Model"] = "test-model", ["Gemini:ApiKey"] = "synthetic", ["Gemini:Model"] = "gemini-test"
        }).Build();
        var router = new AiRouter([new GroqAiProvider(new(groq.Object), config, NullLogger<GroqAiProvider>.Instance, f.Admission()),
            new GeminiAiProvider(new(gemini.Object), config, NullLogger<GeminiAiProvider>.Instance, f.Admission())], NullLogger<AiRouter>.Instance);
        await Assert.ThrowsAsync<AiBudgetDeniedException>(() => router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "contract", "synthetic", true, default));
        await Assert.ThrowsAsync<AiBudgetDeniedException>(() => router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "contract", "synthetic", true, default));
        groq.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        gemini.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        f.Now = f.Now.AddSeconds(31);
        Assert.Equal("ok", await router.ExecuteTaskAsync(AiTaskType.IntentExtraction, "contract", "synthetic", true, default));
        groq.Protected().Verify("SendAsync", Times.Exactly(2), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        static HttpResponseMessage RateLimited() { var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)); return response; }
    }

    [Fact]
    public async Task A_failed_http_attempt_stays_charged_and_a_store_failure_fails_closed()
    {
        var f = new Fixture(); f.Pool.RequestsPerMinute = 2; using var scope = AiTelemetry.Begin(Guid.NewGuid(), AiTaskType.IntentExtraction);
        var http = new Mock<HttpMessageHandler>();
        http.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>()).ThrowsAsync(new TimeoutException());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Groq:ApiKey"] = "synthetic", ["Groq:Model"] = "test-model" }).Build();
        var provider = new GroqAiProvider(new(http.Object), config, NullLogger<GroqAiProvider>.Instance, f.Admission());
        await Assert.ThrowsAsync<TimeoutException>(() => provider.GenerateTextAsync("contract", "synthetic", true, default));
        await Assert.ThrowsAsync<AiBudgetDeniedException>(() => provider.GenerateTextAsync("contract", "synthetic", true, default));
        f.Store.ReadFailure = new IOException("synthetic store failure");
        await Assert.ThrowsAsync<IOException>(() => provider.GenerateTextAsync("contract", "synthetic", true, default));
        http.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_global_quota_never_calls_providers_and_does_not_block_a_zero_token_menu()
    {
        var budget = new Fixture(); budget.Options.Pools.Clear();
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true);
        var http = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Groq:ApiKey"] = "synthetic", ["Groq:Model"] = "test-model", ["Gemini:ApiKey"] = "synthetic", ["Gemini:Model"] = "gemini-test"
        }).Build();
        var router = new AiRouter([new GroqAiProvider(new(http.Object), config, NullLogger<GroqAiProvider>.Instance, budget.Admission()),
            new GeminiAiProvider(new(http.Object), config, NullLogger<GeminiAiProvider>.Instance, budget.Admission())], NullLogger<AiRouter>.Instance);
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => f.Now);
        var interpreter = new AiInterpreter(router, clock.Object, f.ProfileReads.Object, NullLogger<AiInterpreter>.Instance);
        const string text = "Necesito explicarlo de otra manera";
        f.InterpretationOverride = () => interpreter.InterpretAsync(f.Workspace, text, "", null, f.Modules, default);
        await f.HandleTurn(text, "unknown"); Assert.Equal(1, f.AiCalls);
        Assert.DoesNotContain("alcanzó", f.Sent["unknown"].Content);
        Assert.Contains("interpretar", f.Sent["unknown"].Content);
        await f.HandleTurn("menú", "menu"); Assert.Equal(1, f.AiCalls);
        Assert.NotEmpty((await f.Context()).RecoveryOptions); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
        http.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }
}
