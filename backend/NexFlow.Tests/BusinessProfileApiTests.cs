using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NexFlow.API.Controllers.Business;
using NexFlow.API.Security;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Entities;
using Xunit;

namespace NexFlow.Tests;

public sealed class BusinessProfileApiTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("America/New_York")]
    [InlineData("SA Pacific Standard Time")]
    [InlineData("UTC")]
    [InlineData("america/lima")]
    [InlineData("America/Lima ")]
    public async Task Non_peru_zone_is_validation_error_before_profile_or_workspace_writes(string? timeZone)
    {
        await using var f = new ApiFixture();
        var profile = new BusinessProfileDto("Updated name", "", "", "", "", timeZone!);
        var result = Assert.IsType<BadRequestObjectResult>(await f.Controller.SaveProfile(profile, default));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.Equal("Validation.Error", body.RootElement.GetProperty("code").GetString());
        f.VerifyNoPersistence();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("America/New_York")]
    [InlineData("SA Pacific Standard Time")]
    public async Task Http_invalid_zone_cannot_reach_persistence(string? timeZone)
    {
        await using var f = new ApiFixture();
        var client = await f.StartAsync();
        using var response = await client.PutAsJsonAsync("/api/business/profile", Payload(timeZone));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        f.VerifyNoPersistence();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Peru_profile_and_omitted_zone_preserve_authenticated_workspace(bool omitZone)
    {
        await using var f = new ApiFixture();
        var client = await f.StartAsync();
        var payload = Payload("America/Lima");
        if (omitZone) payload.Remove("timeZone");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/business/profile?workspaceId={Guid.NewGuid()}")
            { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Workspace-Id", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        f.Profiles.Verify(p => p.SaveProfileAsync(f.Workspace.Id,
            It.Is<BusinessProfileDto>(p => p.TimeZone == "America/Lima" && p.CommercialName == "Updated name"),
            It.IsAny<CancellationToken>()), Times.Once);
        f.Workspaces.Verify(w => w.GetByIdAsync(f.Workspace.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Updated name", f.Workspace.Name);
        f.Profiles.VerifyNoOtherCalls(); f.Workspaces.VerifyNoOtherCalls(); f.UnitOfWork.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Missing_license_or_update_permission_remains_403_without_persistence(bool licensed, bool canUpdate)
    {
        await using var f = new ApiFixture(licensed, canUpdate);
        var client = await f.StartAsync();
        using var response = await client.PutAsJsonAsync("/api/business/profile", Payload("America/New_York"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        f.VerifyNoPersistence();
    }

    private static Dictionary<string, string?> Payload(string? zone) => new()
    {
        ["commercialName"] = "Updated name", ["taxId"] = "", ["contactEmail"] = "",
        ["whatsAppNumber"] = "", ["description"] = "", ["timeZone"] = zone
    };

    private sealed class ApiFixture : IAsyncDisposable
    {
        public Workspace Workspace { get; } = Workspace.Create("Original name");
        public Mock<IBusinessProfileRepository> Profiles { get; } = new(MockBehavior.Strict);
        public Mock<IWorkspaceRepository> Workspaces { get; } = new(MockBehavior.Strict);
        public Mock<IUnitOfWork> UnitOfWork { get; } = new(MockBehavior.Strict);
        public BusinessController Controller { get; }
        private readonly Mock<IWorkspaceContext> _workspaceContext = new();
        private readonly Mock<IEntitlementService> _entitlements = new(MockBehavior.Strict);
        private WebApplication? _app;
        private HttpClient? _client;

        public ApiFixture(bool licensed = true, bool canUpdate = true)
        {
            _workspaceContext.SetupGet(w => w.CurrentWorkspaceId).Returns(Workspace.Id);
            _entitlements.Setup(e => e.GetAvailableModuleCodesAsync(Workspace.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(licensed ? new[] { "BUSINESS_PROFILE" } : Array.Empty<string>());
            _entitlements.Setup(e => e.HasCapabilityAccessAsync(Workspace.Id, "BUSINESS_PROFILE", "UPDATE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(canUpdate);
            Profiles.Setup(p => p.SaveProfileAsync(Workspace.Id, It.IsAny<BusinessProfileDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Workspaces.Setup(w => w.GetByIdAsync(Workspace.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Workspace);
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Controller = new(Profiles.Object, Mock.Of<ICatalogRepository>(), Mock.Of<IFaqRepository>(),
                Mock.Of<ILocationRepository>(), Mock.Of<IBusinessHoursRepository>(), _workspaceContext.Object,
                Workspaces.Object, UnitOfWork.Object, _entitlements.Object);
        }

        public void VerifyNoPersistence()
        {
            Profiles.VerifyNoOtherCalls(); Workspaces.VerifyNoOtherCalls(); UnitOfWork.VerifyNoOtherCalls();
            Assert.Equal("Original name", Workspace.Name);
        }

        public async Task<HttpClient> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath(),
                ApplicationName = typeof(BusinessController).Assembly.GetName().Name
            });
            builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(_workspaceContext.Object); builder.Services.AddSingleton(_entitlements.Object);
            builder.Services.AddSingleton(Profiles.Object); builder.Services.AddSingleton(Workspaces.Object); builder.Services.AddSingleton(UnitOfWork.Object);
            builder.Services.AddSingleton(Mock.Of<ICatalogRepository>()); builder.Services.AddSingleton(Mock.Of<IFaqRepository>());
            builder.Services.AddSingleton(Mock.Of<ILocationRepository>()); builder.Services.AddSingleton(Mock.Of<IBusinessHoursRepository>());
            builder.Services.AddSingleton(Mock.Of<ICurrentUser>()); builder.Services.AddScoped<TenantCapabilityFilter>();
            // Local MVC transport and strict mocks only: no production startup or external persistence.
            builder.Services.AddAuthorization(options => options.AddPolicy("WorkspaceMember", policy => policy.RequireAssertion(_ => true)));
            builder.Services.AddControllers(options => options.Filters.AddService<TenantCapabilityFilter>())
                .AddApplicationPart(typeof(BusinessController).Assembly)
                .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new BusinessOnly()));
            _app = builder.Build(); _app.UseAuthorization(); _app.MapControllers(); await _app.StartAsync();
            var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            _client = new HttpClient { BaseAddress = new Uri(address) };
            return _client;
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        }
    }

    private sealed class BusinessOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.Where(c => c.AsType() != typeof(BusinessController)).ToArray())
                feature.Controllers.Remove(controller);
        }
    }
}
