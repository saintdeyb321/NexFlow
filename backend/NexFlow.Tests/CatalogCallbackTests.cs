using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using NexFlow.API.Controllers.Webhooks;
using NexFlow.Application.Common;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Tests.Fakes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace NexFlow.Tests;

public sealed class CatalogCallbackTests
{
    [Fact]
    public async Task Obsolete_callback_does_not_replace_newer_generation_or_previous_pdf()
    {
        var f = new CatalogFixture();
        var workspace = Workspace.Create("Callback fixture");
        await f.SeedCurrent(workspace: workspace.Id);
        var active = await f.Generate(workspace: workspace.Id);
        var result = await Callback(f, workspace, "old-generation", active.SourceHash);
        Assert.IsType<OkObjectResult>(result);
        var current = (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!;
        Assert.Equal(active.GenerationId, current.GenerationId);
        Assert.Equal(CatalogArtifactStatus.Generating, current.Status);
        Assert.Equal(active.PdfUrl, current.PdfUrl);
    }

    [Fact]
    public async Task Valid_signed_callback_is_the_point_that_replaces_previous_pdf()
    {
        var f = new CatalogFixture();
        var workspace = Workspace.Create("Callback fixture");
        var previous = await f.SeedCurrent(workspace: workspace.Id);
        var active = await f.Generate(workspace: workspace.Id);
        Assert.Equal(previous.PdfUrl, active.PdfUrl);
        Assert.IsType<OkObjectResult>(await Callback(f, workspace, active.GenerationId!, active.SourceHash));
        var current = (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!;
        Assert.Equal("https://storage.example.test/generated.pdf", current.PdfUrl);
        Assert.Equal(CatalogArtifactStatus.Current, current.Status);
        Assert.Equal(CatalogArtifactOrigin.Generated, current.Origin);
    }

    [Fact]
    public async Task Invalid_signature_cannot_publish_pdf()
    {
        var f = new CatalogFixture();
        var workspace = Workspace.Create("Callback fixture");
        var active = await f.Generate(workspace: workspace.Id);
        Assert.IsType<UnauthorizedObjectResult>(await Callback(f, workspace, active.GenerationId!, active.SourceHash, invalidSignature: true));
        Assert.Equal(CatalogArtifactStatus.Generating, (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!.Status);
    }

    [Fact]
    public async Task Callback_remains_valid_after_sql_commit_acknowledgement_failure()
    {
        var f = new CatalogFixture();
        var workspace = Workspace.Create("Callback fixture");
        await f.SeedCurrent(workspace: workspace.Id);
        f.UnitOfWork.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("SQL acknowledgement lost"));
        await Assert.ThrowsAsync<ArtifactDependencyException>(() => f.Generate(workspace: workspace.Id));
        var active = (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!;
        Assert.IsType<OkObjectResult>(await Callback(f, workspace, active.GenerationId!, active.SourceHash));
        Assert.Equal(CatalogArtifactStatus.Current, (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!.Status);
    }

    [Fact]
    public async Task Changed_business_hash_rejects_callback_and_preserves_previous_pdf()
    {
        var f = new CatalogFixture();
        var workspace = Workspace.Create("Callback fixture");
        var previous = await f.SeedCurrent(workspace: workspace.Id);
        var active = await f.Generate(workspace: workspace.Id);
        Assert.IsType<BadRequestObjectResult>(await Callback(f, workspace, active.GenerationId!, "invalid-hash"));
        var current = (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!;
        Assert.Equal(CatalogArtifactStatus.Failed, current.Status);
        Assert.Equal(previous.PdfUrl, current.PdfUrl);
    }

    private static async Task<IActionResult> Callback(CatalogFixture f, Workspace workspace, string generationId, string hash, bool invalidSignature = false)
    {
        // A test-only relational database supplies the existing lifecycle transaction and workspace check.
        // Advisory functions are no-ops here; this does not exercise or alter a PostgreSQL schema.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction<string, long, long>("hashtextextended", (_, _) => 1);
        connection.CreateFunction<long, long>("pg_advisory_xact_lock_shared", _ => 0);
        await using var db = new CallbackDb(new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        const string secret = "unit-test-secret";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["N8n:WebhookSecret"] = secret }).Build();
        var controller = new N8nWebhookController(f.Artifacts, config) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var body = JsonSerializer.Serialize(new CatalogReadyPayload { WorkspaceId = workspace.Id, Scope = "PRODUCT", GenerationId = generationId, SourceHash = hash, PdfUrl = "https://storage.example.test/generated.pdf" });
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
        return await controller.OnCatalogReady(invalidSignature ? "invalid" : signature, timestamp, f.Service, db, default);
    }

    private sealed class CallbackDb(DbContextOptions<NexFlowDbContext> options) : NexFlowDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToArray()) modelBuilder.Ignore(entity.ClrType);
            modelBuilder.Entity<Workspace>().HasKey(w => w.Id);
            modelBuilder.Entity<Workspace>().Ignore(w => w.DomainEvents);
        }
    }
}
