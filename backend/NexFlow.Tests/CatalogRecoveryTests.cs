using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Infrastructure.Workers;
using NexFlow.Infrastructure.Gateways;
using NexFlow.Tests.Fakes;
using System.Net;
using Xunit;

namespace NexFlow.Tests;

public sealed class CatalogRecoveryTests
{
    [Theory]
    [InlineData("PRODUCT")]
    [InlineData("SERVICE")]
    public async Task Deadline_is_five_minutes_preserves_pdf_and_allows_retry_without_refund(string scope)
    {
        // This case tests a retry within one daily quota window, not its reset at midnight.
        var f = new CatalogFixture { Now = DateTime.UtcNow.Date.AddHours(12) };
        var old = await f.SeedCurrent(scope);
        var active = await f.Generate(scope);
        f.Now = active.GenerationStartedAt!.Value.AddMinutes(5).AddTicks(-1);
        await f.Service.ReconcileGenerationAsync(f.WorkspaceId, scope, default);
        Assert.Equal(CatalogArtifactStatus.Generating, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, scope, default))!.Status);
        f.Now = f.Now.AddTicks(1);
        await f.Service.ReconcileGenerationAsync(f.WorkspaceId, scope, default);
        var failed = (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, scope, default))!;
        Assert.Equal(CatalogArtifactStatus.Failed, failed.Status);
        Assert.Equal("Catalog.GenerationTimeout", failed.FailureCode);
        Assert.Equal(old.PdfUrl, failed.PdfUrl);
        Assert.Equal(1, f.Usage);
        Assert.NotEqual(active.GenerationId, (await f.Generate(scope)).GenerationId);
        Assert.Equal(2, f.Usage);
    }

    [Fact]
    public async Task Deadline_configuration_does_not_confuse_http_timeout_with_async_timeout()
    {
        var f = new CatalogFixture(2);
        var active = await f.Generate();
        f.Now = active.GenerationStartedAt!.Value.AddSeconds(31);
        await f.Service.ReconcileGenerationAsync(f.WorkspaceId, "PRODUCT", default);
        Assert.Equal(CatalogArtifactStatus.Generating, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
        f.Now = active.GenerationStartedAt.Value.AddMinutes(2);
        Assert.Equal(CatalogArtifactStatus.Failed, (await f.Service.GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
    }

    [Fact]
    public async Task Concurrent_cancel_is_idempotent_and_stale_cancel_cannot_fail_new_generation()
    {
        var f = new CatalogFixture();
        var active = await f.Generate();
        f.Firestore.SynchronizeReads(f.ArtifactPath());
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => f.Service.CancelGenerationAsync(f.WorkspaceId, "PRODUCT", active.GenerationId!, default)));
        Assert.All(results, a => Assert.Equal("Catalog.GenerationCancelled", a.FailureCode));
        Assert.Equal(1, f.Usage);
        var next = await f.Generate();
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Service.CancelGenerationAsync(f.WorkspaceId, "PRODUCT", active.GenerationId!, default));
        Assert.Equal(next.GenerationId, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.GenerationId);
        Assert.Equal(2, f.Usage);
    }

    [Fact]
    public async Task Failed_delivery_requires_terminal_sql_status_matching_tenant_scope_id_and_hash()
    {
        var f = new CatalogFixture();
        var product = await f.Generate();
        var service = await f.Generate("SERVICE");
        var message = f.Messages[0];
        message.Status = OutboxStatus.Pending;
        await f.Service.ReconcileFailedDeliveryAsync(message, default);
        Assert.Equal(CatalogArtifactStatus.Generating, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
        message.Status = OutboxStatus.Failed;
        var originalWorkspace = message.WorkspaceId;
        message.WorkspaceId = Guid.NewGuid();
        await f.Service.ReconcileFailedDeliveryAsync(message, default);
        message.WorkspaceId = originalWorkspace;
        Assert.Equal(CatalogArtifactStatus.Generating, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
        await f.Service.ReconcileFailedDeliveryAsync(message, default);
        Assert.Equal("Catalog.GenerationDeliveryFailed", (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.FailureCode);
        Assert.Equal(service.GenerationId, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "SERVICE", default))!.GenerationId);
        var next = await f.Generate();
        await f.Service.ReconcileFailedDeliveryAsync(message, default);
        Assert.NotEqual(product.GenerationId, next.GenerationId);
        Assert.Equal(CatalogArtifactStatus.Generating, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
        Assert.Equal(3, f.Usage);
    }

    [Fact]
    public async Task Recovery_worker_expires_accepted_jobs_without_any_page_get()
    {
        var f = new CatalogFixture();
        var workspace = Workspace.Create("Worker test");
        var product = await f.Generate(workspace: workspace.Id);
        await f.Generate("SERVICE", workspace: workspace.Id);
        f.Now = product.GenerationStartedAt!.Value.AddMinutes(5);
        await using var db = await TestDb.CreateAsync(workspace);
        var services = new ServiceCollection().AddSingleton<NexFlowDbContext>(db)
            .AddSingleton<ICatalogGenerationService>(f.Service).BuildServiceProvider();
        using (services)
        {
            var worker = new CatalogGenerationRecoveryWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<CatalogGenerationRecoveryWorker>.Instance);
            await worker.RunOnceAsync(default);
        }
        foreach (var scope in new[] { "PRODUCT", "SERVICE" })
            Assert.Equal("Catalog.GenerationTimeout", (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, scope, default))!.FailureCode);
    }

    [Theory]
    [InlineData(404, 1, false)]
    [InlineData(503, 1, false)]
    [InlineData(0, 1, false)]
    [InlineData(404, 5, true)]
    [InlineData(503, 5, true)]
    [InlineData(0, 5, true)]
    public async Task Outbox_transient_errors_retry_but_terminal_errors_persist_failed(int status, int attempt, bool terminal)
    {
        var f = new CatalogFixture();
        var workspace = Workspace.Create("Outbox test");
        var old = await f.SeedCurrent(workspace: workspace.Id);
        await f.Generate(workspace: workspace.Id);
        var message = Assert.Single(f.Messages);
        message.Status = OutboxStatus.Processing; message.RetryCount = attempt; message.ProcessingStartedAt = DateTime.UtcNow;
        await using var db = await TestDb.CreateAsync(workspace, message);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["N8n:BaseUrl"] = "https://n8n.example.test", ["N8n:WebhookSecret"] = "unit-test", ["N8n:EventsWebhookId"] = "events"
        }).Build();
        using var http = new HttpClient(new FailingHandler(status));
        var gateway = new N8nWorkflowGateway(http, config);
        using var services = new ServiceCollection().AddSingleton<NexFlowDbContext>(db)
            .AddSingleton<IOutboxRepository>(new PostgresOutboxRepository(db))
            .AddSingleton<ICatalogGenerationService>(f.Service).AddSingleton<ICatalogArtifactRepository>(f.Artifacts)
            .AddSingleton<NexFlow.Application.Abstractions.Integrations.IWorkflowGateway>(gateway).BuildServiceProvider();
        var worker = new OutboxProcessorWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxProcessorWorker>.Instance, config);
        await worker.ProcessClaimedAsync(services, message, default);
        db.ChangeTracker.Clear();
        Assert.Equal(terminal ? OutboxStatus.Failed : OutboxStatus.Pending, (await db.OutboxMessages.SingleAsync()).Status);
        var artifact = (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!;
        Assert.Equal(terminal ? CatalogArtifactStatus.Failed : CatalogArtifactStatus.Generating, artifact.Status);
        Assert.Equal(old.PdfUrl, artifact.PdfUrl);
    }

    private sealed class FailingHandler(int status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => status == 0 ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("Simulated delivery timeout"))
                : Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
    }

    internal sealed class TestDb(DbContextOptions<NexFlowDbContext> options, SqliteConnection connection) : NexFlowDbContext(options)
    {
        public static async Task<TestDb> CreateAsync(Workspace workspace, OutboxMessage? message = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            connection.CreateFunction<string, long, long>("hashtextextended", (_, _) => 1);
            connection.CreateFunction<long, long>("pg_advisory_xact_lock_shared", _ => 0);
            var db = new TestDb(new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(connection).Options, connection);
            await db.Database.EnsureCreatedAsync(); db.Workspaces.Add(workspace);
            if (message != null) db.OutboxMessages.Add(message);
            await db.SaveChangesAsync(); return db;
        }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            foreach (var entity in builder.Model.GetEntityTypes().ToArray()) builder.Ignore(entity.ClrType);
            builder.Entity<Workspace>().HasKey(w => w.Id); builder.Entity<Workspace>().Ignore(w => w.DomainEvents);
            builder.Entity<OutboxMessage>().HasKey(m => m.Id);
        }
        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
