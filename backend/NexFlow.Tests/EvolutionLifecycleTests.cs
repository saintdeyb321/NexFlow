using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.API.Controllers.Business;
using NexFlow.API.Middleware;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class EvolutionLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Middleware_closes_transaction_before_evolution_but_preserves_other_endpoint_locks(bool externalOperation)
    {
        await using var f = await EvolutionFixture.CreateAsync();
        // SQLite executes real transactions. Advisory lock correctness remains a PostgreSQL integration check.
        f.Connection.CreateFunction<string, long, long>("hashtextextended", (_, _) => 0);
        f.Connection.CreateFunction<long, long>("pg_advisory_xact_lock_shared", _ => 0);
        NexFlowDbContext? lifecycleDb = null;
        using var services = new ServiceCollection().AddScoped<NexFlowDbContext>(_ => lifecycleDb = f.NewContext()).BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Workspace-Id"] = f.Workspace.Id.ToString();
        context.Request.Path = "/api/business/whatsapp/connect";
        if (externalOperation) context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ReleaseTenantLifecycleLockAttribute()), "WhatsApp"));
        var userId = Guid.NewGuid();
        var current = new Mock<ICurrentUser>();
        current.SetupGet(u => u.UserId).Returns(userId); current.SetupGet(u => u.IsAuthenticated).Returns(true);
        var memberships = new Mock<IMembershipRepository>();
        memberships.Setup(r => r.GetUserMembershipAsync(userId, f.Workspace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership.Create(userId, f.Workspace.Id, MembershipRole.Owner));
        var workspaces = new Mock<IWorkspaceRepository>();
        workspaces.Setup(r => r.GetByIdForSuperAdminAsync(f.Workspace.Id, It.IsAny<CancellationToken>())).ReturnsAsync(f.Workspace);
        var called = false;
        var middleware = new TenantIsolationMiddleware(async request =>
        {
            called = true;
            Assert.Equal(f.Workspace.Id, request.Items["VerifiedWorkspaceId"]);
            Assert.NotNull(lifecycleDb);
            if (externalOperation)
            {
                Assert.Null(lifecycleDb.Database.CurrentTransaction);
                f.Transport.BeforeRequest = () => { Assert.Null(lifecycleDb.Database.CurrentTransaction); Assert.Null(f.Db.Database.CurrentTransaction); };
                Assert.Equal("QR_AVAILABLE", (await f.Service.ConnectAsync(f.Workspace.Id, default)).Status);
            }
            else Assert.NotNull(lifecycleDb.Database.CurrentTransaction);
        }, NullLogger<TenantIsolationMiddleware>.Instance);
        await middleware.InvokeAsync(context, current.Object, memberships.Object, Mock.Of<ISystemAdministratorRepository>(), workspaces.Object, services.GetRequiredService<IServiceScopeFactory>());
        Assert.True(called);
        if (!externalOperation) Assert.Empty(f.Transport.Calls);
    }

    [Fact]
    public async Task Short_lifecycle_actions_still_reject_foreign_workspaces_before_any_provider_or_database_operation()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers["X-Workspace-Id"] = Guid.NewGuid().ToString();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ReleaseTenantLifecycleLockAttribute()), "WhatsApp"));
        var current = new Mock<ICurrentUser>();
        current.SetupGet(u => u.UserId).Returns(Guid.NewGuid()); current.SetupGet(u => u.IsAuthenticated).Returns(true);
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var middleware = new TenantIsolationMiddleware(_ => throw new InvalidOperationException("Unauthorized downstream execution"), NullLogger<TenantIsolationMiddleware>.Instance);
        await middleware.InvokeAsync(context, current.Object, Mock.Of<IMembershipRepository>(), Mock.Of<ISystemAdministratorRepository>(), Mock.Of<IWorkspaceRepository>(), scopes.Object);
        Assert.Equal(403, context.Response.StatusCode);
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Actual_mvc_routing_releases_lifecycle_transactions_only_for_whatsapp_actions()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", ContentRootPath = Path.GetTempPath(), ApplicationName = typeof(BusinessController).Assembly.GetName().Name });
        builder.Services.AddControllers().AddApplicationPart(typeof(BusinessController).Assembly);
        await using var app = builder.Build();
        app.MapControllers(); // Build actual MVC metadata without starting a listener or the production application.
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ReleaseTenantLifecycleLockAttribute>() != null).Select(e => e.RoutePattern.RawText).Order().ToArray();
        Assert.Equal(new[] { "api/business/whatsapp/connect", "api/business/whatsapp/disconnect", "api/business/whatsapp/status" }, routes);
    }

    [Fact]
    public async Task Workspace_deletion_is_rejected_while_a_durable_whatsapp_operation_is_running()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var operation = await f.Repository.AcquireAsync(f.Workspace.Id, false, f.Time.UtcNow, f.Time.UtcNow.AddMinutes(2), default);
        f.Connection.CreateFunction<string, long, long>("hashtextextended", (_, _) => 0);
        f.Connection.CreateFunction<long, long>("pg_advisory_xact_lock", _ => 0);
        await using var db = f.NewContext(new SqliteRowLockAdapter());
        var scheduler = new TenantDeletionScheduler(db, Mock.Of<IEntitlementService>(), NullLogger<TenantDeletionScheduler>.Instance);
        await Assert.ThrowsAsync<ConcurrencyException>(() => scheduler.RequestAsync(f.Workspace.Id, Guid.NewGuid(), default));
        Assert.Equal(WorkspaceStatus.Active, (await f.Db.Workspaces.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(operation, (await f.State()).OperationId);
        Assert.Empty(f.Transport.Calls);
    }

    // This adapter tests the deletion guard on SQLite; it does not simulate PostgreSQL row/advisory lock semantics.
    private sealed class SqliteRowLockAdapter : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            command.CommandText = command.CommandText.Replace(" FOR UPDATE", string.Empty, StringComparison.Ordinal);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
