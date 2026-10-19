using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.API.Controllers.Business;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Catalog.Uploads;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3Sprint3ArtifactConsistencyTests
{
    [Theory]
    [InlineData("PRODUCT", false)] [InlineData("SERVICE", false)]
    [InlineData("PRODUCT", true)] [InlineData("SERVICE", true)]
    public async Task Confirmed_mutation_is_already_stale_before_any_queue_or_get_and_survives_service_restart(string scope, bool uploaded)
    {
        var f = await Create(); var old = await Seed(f, scope, uploaded);
        await Edit(f, scope, "Changed");
        var persisted = (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, scope, default))!;
        Assert.Equal(CatalogArtifactStatus.Stale, persisted.Status); Assert.Equal(old.PdfUrl, persisted.PdfUrl);
        var restarted = Restart(f);
        Assert.Equal(CatalogArtifactStatus.Stale, (await restarted.GetArtifactAsync(f.WorkspaceId, scope, default))!.Status);
        Assert.Equal(0, f.Usage); Assert.Empty(f.Messages);
        Assert.Equal(uploaded ? CatalogArtifactOrigin.Uploaded : CatalogArtifactOrigin.Generated, persisted.Origin);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Shared_category_edit_or_delete_atomically_invalidates_both_scopes(bool delete)
    {
        var f = await Create(); var repo = new FirestoreCatalogRepository(f.Firestore.Db);
        var category = new BusinessCategoryDto { Id = "shared", Name = "Shared", Scope = "SHARED", IsActive = true };
        await repo.SaveCategoryAsync(f.WorkspaceId, category, true, default);
        await Seed(f, "PRODUCT"); await Seed(f, "SERVICE");
        if (delete) await repo.DeleteCategoryAsync(f.WorkspaceId, category.Id, default);
        else { category.Description = "New metadata"; await repo.SaveCategoryAsync(f.WorkspaceId, category, false, default); }
        foreach (var scope in new[] { "PRODUCT", "SERVICE" })
        {
            Assert.Equal(CatalogArtifactStatus.Stale, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, scope, default))!.Status);
            Assert.Equal(CatalogArtifactStatus.Stale, (await f.Service.GetArtifactAsync(f.WorkspaceId, scope, default))!.Status);
        }
    }

    [Fact]
    public async Task Failed_invalidation_commit_cannot_leave_a_confirmed_source_edit_and_lost_ack_is_durable()
    {
        var f = await Create(); await Seed(f, "PRODUCT");
        f.Firestore.BeforeCommit = _ => new IOException("Simulated Firestore outage");
        await Assert.ThrowsAsync<IOException>(() => Edit(f, "PRODUCT", "Not committed"));
        f.Firestore.BeforeCommit = null;
        Assert.Equal("Original", (await new FirestoreCatalogRepository(f.Firestore.Db).GetItemByIdAsync(f.WorkspaceId, "product", default))!.Name);
        Assert.Equal(CatalogArtifactStatus.Current, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
        f.Firestore.AfterCommit = _ => new IOException("Simulated crash after commit");
        await Assert.ThrowsAsync<IOException>(() => Edit(f, "PRODUCT", "Committed"));
        f.Firestore.AfterCommit = null;
        Assert.Equal(CatalogArtifactStatus.Stale, (await Restart(f).GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
    }

    [Fact]
    public async Task Failed_source_read_never_returns_current_and_revalidation_recovers_after_restart()
    {
        var f = await Create(); var previous = await Seed(f, "PRODUCT"); await Edit(f, "PRODUCT", "Changed");
        f.Firestore.ReadFailure = new IOException("Simulated dependency unavailable");
        await Assert.ThrowsAsync<IOException>(() => f.Service.GetArtifactAsync(f.WorkspaceId, "PRODUCT", default));
        f.Firestore.ReadFailure = null;
        var artifact = (await Restart(f).GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!;
        Assert.Equal(CatalogArtifactStatus.Stale, artifact.Status); Assert.Equal(previous.PdfUrl, artifact.PdfUrl);
    }

    [Fact]
    public async Task Identical_hash_and_design_reuse_generated_pdf_without_quota_but_uploaded_pdf_can_be_replaced()
    {
        var f = await Create(); var previous = await Seed(f, "PRODUCT");
        await Edit(f, "PRODUCT", "Original"); // Conservative fence, identical effective source.
        var result = await f.Generate();
        Assert.Equal(CatalogArtifactStatus.Current, result.Status); Assert.Equal(previous.PdfUrl, result.PdfUrl);
        Assert.Equal(previous.LastGeneratedAt, result.LastGeneratedAt); Assert.Equal(0, f.Usage); Assert.Empty(f.Messages);
        await Seed(f, "SERVICE", uploaded: true);
        Assert.Equal(CatalogArtifactStatus.Generating, (await f.Generate("SERVICE")).Status); Assert.Equal(1, f.Usage);
    }

    [Fact]
    public async Task Two_concurrent_edits_preserve_staleness_and_other_workspace_and_scope_are_untouched()
    {
        var f = await Create(); var b = Guid.NewGuid();
        await Profile(f, b); await Seed(f, "PRODUCT", workspace: b); await Seed(f, "PRODUCT"); await Seed(f, "SERVICE");
        f.Firestore.SynchronizeReads(f.ArtifactPath());
        await Task.WhenAll(Edit(f, "PRODUCT", "One"), Edit(f, "PRODUCT", "Two"));
        Assert.True(f.Firestore.AbortedCommits > 0);
        Assert.Equal(CatalogArtifactStatus.Stale, (await f.Service.GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
        Assert.Equal(CatalogArtifactStatus.Current, (await f.Service.GetArtifactAsync(f.WorkspaceId, "SERVICE", default))!.Status);
        Assert.Equal(CatalogArtifactStatus.Current, (await f.Service.GetArtifactAsync(b, "PRODUCT", default))!.Status);
    }

    [Fact]
    public async Task Mutation_and_publication_race_cannot_leave_an_old_callback_current()
    {
        var f = await Create(); var active = await f.Generate();
        active.CompleteGeneration("https://storage.example.test/old-source.pdf");
        f.Firestore.SynchronizeReads(f.ArtifactPath());
        async Task Publish() { try { await f.Artifacts.SaveArtifactAsync(active, default); } catch (ConcurrencyException) { } }
        await Task.WhenAll(Publish(), Edit(f, "PRODUCT", "New source"));
        var persisted = (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!;
        Assert.NotEqual(CatalogArtifactStatus.Current, persisted.Status);
        Assert.NotEqual(CatalogArtifactStatus.Current, (await f.Service.GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
    }

    [Fact]
    public async Task Simultaneous_mutations_in_two_workspaces_keep_their_sources_and_artifacts_isolated()
    {
        var f = await Create(); var b = Guid.NewGuid(); await Profile(f, b);
        var repo = new FirestoreCatalogRepository(f.Firestore.Db);
        await repo.SaveItemAsync(b, Product("Original"), true, default); await Seed(f, "PRODUCT"); await Seed(f, "PRODUCT", workspace: b);
        await Task.WhenAll(Edit(f, "PRODUCT", "Workspace A"), repo.SaveItemAsync(b, Product("Workspace B"), false, default));
        Assert.Equal("Workspace A", (await repo.GetItemByIdAsync(f.WorkspaceId, "product", default))!.Name);
        Assert.Equal("Workspace B", (await repo.GetItemByIdAsync(b, "product", default))!.Name);
        foreach (var workspace in new[] { f.WorkspaceId, b })
            Assert.Equal(CatalogArtifactStatus.Stale, (await f.Service.GetArtifactAsync(workspace, "PRODUCT", default))!.Status);
    }

    [Fact]
    public async Task Source_change_cancels_upload_publication_and_existing_recovery_cleans_only_the_unreferenced_asset()
    {
        var f = await Create(); var old = await Seed(f, "PRODUCT", uploaded: true);
        f.Storage.Setup(s => s.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (Stream _, string name, string folder, CancellationToken _) =>
            {
                await Edit(f, "PRODUCT", "Changed during upload");
                return new StoredPdfAsset($"https://storage.example.test/{name}", $"{folder}/{name}");
            });
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Upload());
        var artifact = (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!;
        Assert.Equal(old.PdfUrl, artifact.PdfUrl); Assert.Equal(CatalogArtifactStatus.Stale, artifact.Status);
        f.Storage.Verify(s => s.DeletePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("profile")] [InlineData("location")] [InlineData("delete")]
    public async Task Other_effective_source_writers_also_fence_the_artifacts(string mutation)
    {
        var f = await Create(); await Seed(f, "PRODUCT"); await Seed(f, "SERVICE");
        if (mutation == "profile") await new FirestoreBusinessProfileRepository(f.Firestore.Db).SaveProfileAsync(f.WorkspaceId, new("New name", "", "", "", ""), default);
        else if (mutation == "location") await new FirestoreLocationRepository(f.Firestore.Db).SaveLocationAsync(f.WorkspaceId, new("new-location", "Main", "Address", null, null, true), true, 10, default);
        else await new FirestoreCatalogRepository(f.Firestore.Db).DeleteItemAsync(f.WorkspaceId, "product", default);
        Assert.Equal(CatalogArtifactStatus.Stale, (await f.Service.GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
        Assert.Equal(mutation == "delete" ? CatalogArtifactStatus.Current : CatalogArtifactStatus.Stale,
            (await f.Service.GetArtifactAsync(f.WorkspaceId, "SERVICE", default))!.Status);
    }

    [Fact]
    public async Task Signed_duplicate_callback_is_idempotent_and_callback_after_source_edit_cannot_publish()
    {
        var workspace = Workspace.Create("Callback test"); var f = await Create(); await Profile(f, workspace.Id);
        var active = await f.Generate(workspace: workspace.Id);
        Assert.IsType<OkObjectResult>(await CatalogCallbackTests.Callback(f, workspace, active.GenerationId!, active.SourceHash));
        var current = (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!;
        Assert.IsType<OkObjectResult>(await CatalogCallbackTests.Callback(f, workspace, active.GenerationId!, active.SourceHash, pdfUrl: "https://storage.example.test/duplicate.pdf"));
        var duplicate = (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!;
        Assert.Equal(current.PersistenceVersion, duplicate.PersistenceVersion); Assert.Equal(current.PdfUrl, duplicate.PdfUrl);
        await new FirestoreCatalogRepository(f.Firestore.Db).SaveItemAsync(workspace.Id, Product("New source"), true, default);
        var newer = await f.Generate(workspace: workspace.Id);
        await new FirestoreCatalogRepository(f.Firestore.Db).SaveItemAsync(workspace.Id, Product("Changed again"), false, default);
        Assert.IsType<OkObjectResult>(await CatalogCallbackTests.Callback(f, workspace, newer.GenerationId!, newer.SourceHash));
        var rejected = (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!;
        Assert.Equal(CatalogArtifactStatus.Failed, rejected.Status); Assert.Equal(current.PdfUrl, rejected.PdfUrl);
    }

    [Fact]
    public async Task Concurrent_signed_callbacks_publish_once_and_both_acknowledge_the_durable_result()
    {
        var workspace = Workspace.Create("Concurrent callback"); var f = await Create(); await Profile(f, workspace.Id);
        var active = await f.Generate(workspace: workspace.Id); f.Firestore.SynchronizeReads(f.ArtifactPath(workspace: workspace.Id));
        var results = await Task.WhenAll(CatalogCallbackTests.Callback(f, workspace, active.GenerationId!, active.SourceHash),
            CatalogCallbackTests.Callback(f, workspace, active.GenerationId!, active.SourceHash, pdfUrl: "https://storage.example.test/duplicate.pdf"));
        Assert.All(results, r => Assert.IsType<OkObjectResult>(r));
        Assert.Equal(CatalogArtifactStatus.Current, (await f.Artifacts.GetCurrentArtifactAsync(workspace.Id, "PRODUCT", default))!.Status);
        Assert.Single(f.Messages);
    }

    [Theory]
    [InlineData("PRODUCT")] [InlineData("SERVICE")]
    public async Task Artifact_endpoint_never_labels_the_previous_download_current_after_source_change(string scope)
    {
        var f = await Create(); var previous = await Seed(f, scope); await Edit(f, scope, "Changed");
        var workspace = Mock.Of<IWorkspaceContext>(c => c.CurrentWorkspaceId == f.WorkspaceId);
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.GetAvailableModuleCodesAsync(f.WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "CATALOG", "SERVICES" });
        var controller = new CatalogController(new FirestoreCatalogRepository(f.Firestore.Db), workspace, entitlements.Object);
        var result = Assert.IsType<OkObjectResult>(await controller.GetArtifactStatus(scope, f.Service, default));
        var dto = Assert.IsType<ArtifactStatusDto>(result.Value); Assert.Equal("STALE", dto.Status); Assert.Equal(previous.PdfUrl, dto.PdfUrl);
    }

    [Theory]
    [InlineData("PRODUCT")] [InlineData("SERVICE")]
    public async Task Read_rechecks_the_source_fence_even_when_an_interleaved_edit_happened_after_items_were_read(string scope)
    {
        var f = await Create(); await Seed(f, scope); var repo = new FirestoreCatalogRepository(f.Firestore.Db);
        var interleaved = false; var source = new Mock<ICatalogRepository>(MockBehavior.Strict);
        source.Setup(c => c.GetActiveCategoriesAsync(f.WorkspaceId, It.IsAny<CancellationToken>()))
            .Returns((Guid w, CancellationToken ct) => repo.GetActiveCategoriesAsync(w, ct));
        source.Setup(c => c.GetItemsByTypeAsync(f.WorkspaceId, scope, It.IsAny<CancellationToken>()))
            .Returns(async (Guid w, string s, CancellationToken ct) =>
            {
                var snapshot = (await repo.GetItemsByTypeAsync(w, s, ct)).ToArray();
                if (!interleaved) { interleaved = true; await Edit(f, scope, "Changed after snapshot"); }
                return snapshot;
            });
        Assert.Equal(CatalogArtifactStatus.Stale, (await Restart(f, source.Object).GetArtifactAsync(f.WorkspaceId, scope, default))!.Status);
        Assert.True(interleaved);
    }

    [Fact]
    public async Task Reuse_path_also_checks_the_fence_after_its_second_source_read_without_spending_quota()
    {
        var f = await Create(); await Seed(f, "PRODUCT"); var repo = new FirestoreCatalogRepository(f.Firestore.Db);
        var reads = 0; var source = new Mock<ICatalogRepository>(MockBehavior.Strict);
        source.Setup(c => c.GetActiveCategoriesAsync(f.WorkspaceId, It.IsAny<CancellationToken>()))
            .Returns((Guid w, CancellationToken ct) => repo.GetActiveCategoriesAsync(w, ct));
        source.Setup(c => c.GetItemsByTypeAsync(f.WorkspaceId, "PRODUCT", It.IsAny<CancellationToken>()))
            .Returns(async (Guid w, string s, CancellationToken ct) =>
            {
                var snapshot = (await repo.GetItemsByTypeAsync(w, s, ct)).ToArray();
                if (++reads == 2) await Edit(f, "PRODUCT", "Changed during reuse");
                return snapshot;
            });
        await Assert.ThrowsAsync<ConcurrencyException>(() => Restart(f, source.Object).RequestGenerationAsync(f.WorkspaceId, "PRODUCT", CatalogFixture.Design, true, default));
        Assert.Equal(0, f.Usage); Assert.Empty(f.Messages);
        Assert.Equal(CatalogArtifactStatus.Stale, (await f.Service.GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
    }

    private static async Task<CatalogFixture> Create()
    {
        var f = new CatalogFixture(useRealSources: true); await Profile(f, f.WorkspaceId);
        var repo = new FirestoreCatalogRepository(f.Firestore.Db);
        await repo.SaveItemAsync(f.WorkspaceId, Product("Original"), true, default);
        await repo.SaveItemAsync(f.WorkspaceId, Service("Original"), true, default); return f;
    }
    private static Task Profile(CatalogFixture f, Guid w) => new FirestoreBusinessProfileRepository(f.Firestore.Db).SaveProfileAsync(w, new("Business", "", "", "", ""), default);
    private static ProductDto Product(string name) => new() { Id = "product", Name = name, Currency = "PEN", LocationScope = "ALL", IsActive = true, PriceMinorUnits = 100 };
    private static ServiceDto Service(string name) => new() { Id = "service", Name = name, Currency = "PEN", LocationScope = "ALL", IsActive = true, DurationInMinutes = 30, RequiresReservation = true };
    private static Task Edit(CatalogFixture f, string scope, string name) => new FirestoreCatalogRepository(f.Firestore.Db).SaveItemAsync(f.WorkspaceId, scope == "PRODUCT" ? Product(name) : Service(name), false, default);
    private static async Task<CatalogArtifact> Seed(CatalogFixture f, string scope, bool uploaded = false, Guid? workspace = null)
    {
        var artifact = CatalogArtifact.Initialize(workspace ?? f.WorkspaceId, scope);
        var hash = await f.Service.GetCurrentSourceHashAsync(artifact.WorkspaceId, scope, default);
        if (uploaded) artifact.CompleteUpload("https://storage.example.test/previous.pdf", hash);
        else { artifact.MarkAsGenerating(hash, Guid.NewGuid().ToString("N"), CatalogFixture.Design); artifact.CompleteGeneration("https://storage.example.test/previous.pdf"); }
        await f.Artifacts.SaveArtifactAsync(artifact, default); return artifact;
    }
    private static CatalogGenerationService Restart(CatalogFixture f, ICatalogRepository? source = null) => new(source ?? new FirestoreCatalogRepository(f.Firestore.Db), new FirestoreCatalogArtifactRepository(f.Firestore.Db),
        new FirestoreCatalogArtifactRepository(f.Firestore.Db), new CatalogHashService(), new FirestoreBusinessProfileRepository(f.Firestore.Db), new FirestoreLocationRepository(f.Firestore.Db),
        f.Storage.Object, new FirestoreCatalogUploadRepository(f.Firestore.Db), f.Outbox.Object, f.UnitOfWork.Object, NullLogger<CatalogGenerationService>.Instance);
}
