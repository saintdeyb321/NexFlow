using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NexFlow.API.Controllers.Business;
using NexFlow.API.Controllers.Webhooks;
using NexFlow.API.Middleware;
using NexFlow.API.Security;
using NexFlow.API.Services.BackgroundServices;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class CatalogRecoveryHttpTests
{
    [Theory]
    [InlineData("PRODUCT")]
    [InlineData("SERVICE")]
    public async Task Generate_cancel_late_callback_and_retry_preserve_http_contracts_and_quota(string scope)
    {
        await using var f = await Fixture.StartAsync();
        await f.Catalog.SeedCurrent(scope, f.Workspace.Id);
        using var accepted = await f.Client.PostAsJsonAsync("/api/catalog/artifact/generate", Generate(scope, true));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var artifact = await f.Status(scope);
        var generation = artifact.GetProperty("generationId").GetString()!;
        using var cancelled = await f.Client.PostAsJsonAsync("/api/catalog/artifact/cancel", new { scope, generationId = generation });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        using var body = JsonDocument.Parse(await cancelled.Content.ReadAsStringAsync());
        Assert.Equal("FAILED", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Catalog.GenerationCancelled", body.RootElement.GetProperty("failureCode").GetString());
        Assert.Equal("https://storage.example.test/previous.pdf", body.RootElement.GetProperty("pdfUrl").GetString());
        using var callback = await f.Callback(scope, generation, artifact.GetProperty("sourceHash").GetString()!);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Equal("FAILED", (await f.Status(scope)).GetProperty("status").GetString());
        using var retry = await f.Client.PostAsJsonAsync("/api/catalog/artifact/generate", Generate(scope, true));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.NotEqual(generation, (await f.Status(scope)).GetProperty("generationId").GetString());
        Assert.Equal(2, f.Usage);
        using var obsolete = await f.Client.PostAsJsonAsync("/api/catalog/artifact/cancel", new { scope, generationId = generation });
        Assert.Equal(HttpStatusCode.Conflict, obsolete.StatusCode);
        Assert.Equal("GENERATING", (await f.Status(scope)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancellation_requires_authentication_and_generate_permission_of_body_scope()
    {
        await using var f = await Fixture.StartAsync();
        var active = await f.Catalog.Generate("SERVICE", workspace: f.Workspace.Id);
        f.Client.DefaultRequestHeaders.Remove("X-Test-Auth");
        using var anonymous = await f.Client.PostAsJsonAsync("/api/catalog/artifact/cancel", new { scope = "SERVICE", generationId = active.GenerationId });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        f.Client.DefaultRequestHeaders.Add("X-Test-Auth", "yes"); f.ServiceGenerateAllowed = false;
        using var forbidden = await f.Client.PostAsJsonAsync("/api/catalog/artifact/cancel?scope=PRODUCT", new { scope = "SERVICE", generationId = active.GenerationId });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("GENERATING", (await f.Status("SERVICE")).GetProperty("status").GetString());
        Assert.Equal(1, f.Usage);
    }

    [Fact]
    public async Task Valid_callback_within_deadline_publishes_and_invalid_hmac_does_not()
    {
        await using var f = await Fixture.StartAsync();
        var active = await f.Catalog.Generate(workspace: f.Workspace.Id);
        using var invalid = await f.Callback("PRODUCT", active.GenerationId!, active.SourceHash, valid: false);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        using var valid = await f.Callback("PRODUCT", active.GenerationId!, active.SourceHash);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal("CURRENT", (await f.Status("PRODUCT")).GetProperty("status").GetString());
    }

    private static object Generate(string scope, bool replaceCurrent) => new { scope, replaceCurrent, design = new { visualStyle = "MODERN", palette = "OCEAN", creativity = "BALANCED" } };

    private sealed class Fixture : IAsyncDisposable
    {
        public CatalogFixture Catalog { get; } = new();
        public Workspace Workspace { get; } = Workspace.Create("HTTP PDF fixture");
        public bool ServiceGenerateAllowed { get; set; } = true;
        public HttpClient Client { get; private set; } = null!;
        public int Usage => (int)Catalog.Firestore.Read($"workspaces/{Workspace.Id}/catalogGenerationUsages/{DateTime.UtcNow:yyyy-MM-dd}")!.Fields["GenerationCount"].IntegerValue;
        private WebApplication _app = null!;
        private const string Secret = "catalog-http-test-only";
        public static async Task<Fixture> StartAsync()
        {
            var f = new Fixture();
            var db = await CatalogRecoveryTests.TestDb.CreateAsync(f.Workspace);
            var context = new Mock<IWorkspaceContext>(); context.SetupGet(c => c.CurrentWorkspaceId).Returns(f.Workspace.Id);
            var entitlements = new Mock<IEntitlementService>();
            entitlements.Setup(e => e.GetAvailableModuleCodesAsync(f.Workspace.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "CATALOG", "SERVICES" });
            entitlements.Setup(e => e.HasCapabilityAccessAsync(f.Workspace.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string module, string capability, CancellationToken _) => module != "SERVICES" || capability != "GENERATE" || f.ServiceGenerateAllowed);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath(), ApplicationName = typeof(CatalogController).Assembly.GetName().Name });
            builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["N8n:WebhookSecret"] = Secret });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<NexFlowDbContext>(db); builder.Services.AddSingleton<ICatalogGenerationService>(f.Catalog.Service);
            builder.Services.AddSingleton<ICatalogArtifactRepository>(f.Catalog.Artifacts); builder.Services.AddSingleton<ICatalogRepository>(f.Catalog.Catalog.Object);
            builder.Services.AddSingleton(context.Object); builder.Services.AddSingleton(entitlements.Object);
            builder.Services.AddSingleton(Mock.Of<IBackgroundTaskQueue>()); builder.Services.AddSingleton(Mock.Of<ICurrentUser>());
            builder.Services.AddScoped<TenantCapabilityFilter>();
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, LocalAuthentication>("test", _ => { });
            builder.Services.AddAuthorization(o => o.AddPolicy("WorkspaceMember", p => p.RequireAuthenticatedUser()));
            builder.Services.AddControllers(o => o.Filters.AddService<TenantCapabilityFilter>()).AddApplicationPart(typeof(CatalogController).Assembly)
                .ConfigureApplicationPartManager(p => p.FeatureProviders.Add(new CatalogOnly()));
            f._app = builder.Build(); f._app.UseMiddleware<GlobalExceptionMiddleware>(); f._app.UseAuthentication(); f._app.UseAuthorization(); f._app.MapControllers(); await f._app.StartAsync();
            f.Client = new HttpClient { BaseAddress = new Uri(f._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            f.Client.DefaultRequestHeaders.Add("X-Test-Auth", "yes"); return f;
        }
        public async Task<JsonElement> Status(string scope)
        {
            using var response = await Client.GetAsync($"/api/catalog/artifact?scope={scope}"); response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            // SourceHash stays internal; obtain it from the real fixture repository for signed callbacks.
            var artifact = (await Catalog.Artifacts.GetCurrentArtifactAsync(Workspace.Id, scope, default))!;
            var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body.RootElement.GetRawText())!;
            fields["sourceHash"] = JsonSerializer.SerializeToElement(artifact.SourceHash);
            return JsonSerializer.SerializeToElement(fields);
        }
        public Task<HttpResponseMessage> Callback(string scope, string id, string hash, bool valid = true)
        {
            var body = JsonSerializer.Serialize(new CatalogReadyPayload { WorkspaceId = Workspace.Id, Scope = scope, GenerationId = id, SourceHash = hash, PdfUrl = "https://storage.example.test/ready.pdf" });
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/n8n/catalog-ready") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-NexFlow-Timestamp", timestamp);
            request.Headers.Add("X-NexFlow-Signature", valid ? Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))) : "invalid");
            return Client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await _app.StopAsync(); await _app.DisposeAsync(); }
    }

    private sealed class LocalAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.ContainsKey("X-Test-Auth")
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") }, "test")), "test"))
            : AuthenticateResult.NoResult());
    }
    private sealed class CatalogOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var c in feature.Controllers.Where(c => c.AsType() != typeof(CatalogController) && c.AsType() != typeof(N8nWebhookController)).ToArray()) feature.Controllers.Remove(c);
        }
    }
}
