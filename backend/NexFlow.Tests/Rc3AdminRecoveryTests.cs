using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.API.Controllers.SuperAdmin;
using NexFlow.API.Controllers.Business;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Domain.ValueObjects;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.SuperAdmin.Workspaces;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3AdminRecoveryTests
{
    [Fact]
    public async Task Member_list_uses_only_authenticated_workspace_and_keeps_current_contract()
    {
        var workspace = Guid.NewGuid();
        var user = User.Create(new Email("synthetic@example.invalid"), "Synthetic", "User");
        var context = Mock.Of<IWorkspaceContext>(w => w.CurrentWorkspaceId == workspace);
        var memberships = new Mock<IMembershipRepository>(MockBehavior.Strict);
        memberships.Setup(r => r.GetByWorkspaceIdAsync(workspace, default)).ReturnsAsync([Membership.Create(user.Id, workspace, MembershipRole.Member)]);
        var users = new Mock<IUserRepository>(MockBehavior.Strict);
        users.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        var result = Assert.IsType<OkObjectResult>(await new MembersController(context, memberships.Object, users.Object).GetMembers(default));
        var member = Assert.Single(Assert.IsType<List<MemberDto>>(result.Value));
        Assert.Equal(workspace, member.WorkspaceId); Assert.Equal(user.Id, member.UserId); Assert.Equal("Member", member.Role);
        memberships.Verify(r => r.GetByWorkspaceIdAsync(workspace, default), Times.Once); memberships.VerifyNoOtherCalls();
        users.Verify(r => r.GetByIdAsync(user.Id, default), Times.Once); users.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Http_suspension_requires_reason_before_touching_any_business_data()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath() });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthorization(o => o.AddPolicy("SuperAdmin", p => p.RequireAssertion(_ => true)));
        builder.Services.AddControllers().AddApplicationPart(typeof(ClientsController).Assembly)
            .ConfigureApplicationPartManager(p => p.FeatureProviders.Add(new ClientsOnly())).AddControllersAsServices();
        builder.Services.AddSingleton(new ClientsController(null!, null!));
        var repo = new Mock<IWorkspaceRepository>(MockBehavior.Strict);
        builder.Services.AddSingleton(new SuspendClientCommandHandler(repo.Object, Mock.Of<ILicenseRepository>(),
            Mock.Of<IAuditLogRepository>(), Mock.Of<IUnitOfWork>(), Mock.Of<ICurrentUser>()));
        await using var app = builder.Build(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var response = await client.PostAsJsonAsync("/api/superadmin/clients/suspend", new { workspaceId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(json.RootElement.GetProperty("errors").EnumerateObject(), p => p.Name.Equals("Reason", StringComparison.OrdinalIgnoreCase));
        repo.VerifyNoOtherCalls();
    }

    private sealed class ClientsOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.Where(c => c.AsType() != typeof(ClientsController)).ToArray()) feature.Controllers.Remove(controller);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Suspend_then_reactivate_restores_only_suspended_license_without_extending_it(bool permanent)
    {
        var workspace = Workspace.Create("Synthetic");
        var now = DateTime.UtcNow;
        var license = License.CreateCustomLicense(workspace.Id, now, permanent ? null : now.AddDays(30), 1);
        var expiry = license.ValidityPeriod.End;
        var workspaces = new Mock<IWorkspaceRepository>();
        workspaces.Setup(r => r.GetByIdForSuperAdminAsync(workspace.Id, default)).ReturnsAsync(workspace);
        var licenses = new Mock<ILicenseRepository>();
        licenses.Setup(r => r.GetByWorkspaceIdAsync(workspace.Id, default)).ReturnsAsync(license);
        var entitlements = new Mock<IEntitlementService>();
        var unit = new Mock<IUnitOfWork>();
        var services = new ServiceCollection().AddSingleton(workspaces.Object).AddSingleton(licenses.Object)
            .AddSingleton(entitlements.Object).AddSingleton(unit.Object).BuildServiceProvider();
        using (services)
        {
            var suspend = new SuspendClientCommandHandler(workspaces.Object, licenses.Object, Mock.Of<IAuditLogRepository>(), unit.Object, Mock.Of<ICurrentUser>());
            Assert.True((await suspend.Handle(new(workspace.Id, "Explicit administrative suspension"), default)).IsSuccess);
            Assert.Equal(WorkspaceStatus.Suspended, workspace.Status);
            Assert.False(license.IsValidAt(now));
            var reactivate = ActivatorUtilities.CreateInstance<ReactivateClientCommandHandler>(services);
            Assert.True((await reactivate.Handle(new(workspace.Id), default)).IsSuccess);
            Assert.Equal(WorkspaceStatus.Active, workspace.Status);
            Assert.True(license.IsValidAt(now));
            Assert.Equal(expiry, license.ValidityPeriod.End);
            entitlements.Verify(e => e.InvalidateWorkspaceCache(workspace.Id), Times.Once);
        }
    }

    [Fact]
    public void Resuming_suspension_does_not_renew_an_expired_license()
    {
        var now = DateTime.UtcNow;
        var license = License.CreateCustomLicense(Guid.NewGuid(), now.AddDays(-30), now.AddDays(-2), 1);
        var expiry = license.ValidityPeriod.End;
        license.Suspend(); license.Resume();
        Assert.False(license.IsValidAt(now));
        Assert.Equal(expiry, license.ValidityPeriod.End);
    }
}
