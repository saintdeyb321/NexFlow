using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Moq;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Catalog.Uploads;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Exceptions;
using NexFlow.Tests.Fakes;
using Xunit;
using Value = Google.Cloud.Firestore.V1.Value;

namespace NexFlow.Tests;

public sealed class CatalogUploadTests
{
    [Theory]
    [InlineData("brochure.txt", "application/pdf", "%PDF-1.7", 8)]
    [InlineData("brochure.pdf", "image/png", "%PDF-1.7", 8)]
    [InlineData("brochure.pdf", "application/pdf", "INVALID!", 8)]
    [InlineData("brochure.pdf", "application/pdf", "%PDF-1.7", 4)]
    [InlineData("brochure.pdf", "application/pdf", "%PDF-1.7", 15728641)]
    [InlineData("brochure.pdf", "application/pdf", "%PDF-1.7", 9)]
    public async Task Invalid_pdf_is_rejected_before_storage(string name, string mime, string content, long length)
    {
        var f = new CatalogFixture();
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(content));
        await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadPdfAsync(f.WorkspaceId, "PRODUCT", stream, name, mime, length, true, default));
        f.Storage.Verify(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(f.Firestore.Read(f.ArtifactPath()));
    }

    [Fact]
    public async Task Upload_is_forbidden_during_generation()
    {
        var f = new CatalogFixture();
        await f.Generate();
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Upload());
        f.Storage.Verify(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Concurrent_uploads_store_only_one_pdf_and_generation_cannot_take_upload_lease()
    {
        var f = new CatalogFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Storage.Setup(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (Stream _, string name, string folder, CancellationToken _) =>
            { entered.SetResult(); await finish.Task; return new StoredPdfAsset("https://storage.example.test/new.pdf", $"{folder}/{name}"); });
        var first = f.Upload();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.ThrowsAsync<ConcurrencyException>(() => f.Upload());
            await Assert.ThrowsAsync<ConcurrencyException>(() => f.Generate());
            Assert.Equal(0, f.Usage);
        }
        finally { finish.SetResult(); }
        var current = await first;
        Assert.Equal(CatalogArtifactStatus.Current, current.Status);
        Assert.Equal(CatalogArtifactOrigin.Uploaded, current.Origin);
        Assert.Null(current.PendingUploadId);
        f.Storage.Verify(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failure_after_storage_cleans_only_unpublished_pdf_and_preserves_old_pdf()
    {
        var f = new CatalogFixture();
        var old = await f.SeedCurrent();
        string? publicId = null;
        f.Storage.Setup(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string name, string folder, CancellationToken _) =>
            { publicId = $"{folder}/{name}"; return new StoredPdfAsset("https://storage.example.test/unpublished.pdf", publicId); });
        f.Firestore.BeforeCommit = commit => IsPublication(commit) ? new RpcException(new Status(StatusCode.PermissionDenied, "Simulated publication failure")) : null;
        await Assert.ThrowsAsync<ArtifactDependencyException>(() => f.Upload());
        f.Storage.Verify(r => r.DeletePdfAsync(publicId!, It.IsAny<CancellationToken>()), Times.Once);
        var current = (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!;
        Assert.Equal(old.PdfUrl, current.PdfUrl);
        Assert.Null(current.PendingUploadId);
    }

    [Fact]
    public async Task Lost_publication_acknowledgement_never_deletes_committed_pdf()
    {
        var f = new CatalogFixture();
        f.Firestore.AfterCommit = commit => IsPublication(commit) ? new RpcException(new Status(StatusCode.PermissionDenied, "Lost acknowledgement")) : null;
        await Assert.ThrowsAsync<ArtifactDependencyException>(() => f.Upload());
        var current = (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!;
        Assert.Equal(CatalogArtifactStatus.Current, current.Status);
        Assert.Equal(CatalogArtifactOrigin.Uploaded, current.Origin);
        Assert.NotNull(current.PdfStoragePublicId);
        f.Storage.Verify(r => r.DeletePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cleanup_fences_late_publication_before_deleting_asset()
    {
        var f = new CatalogFixture();
        var old = await f.SeedCurrent();
        var operation = PdfUploadOperation.Create(f.WorkspaceId, "PRODUCT", DateTime.UtcNow);
        await f.Uploads.BeginAsync(old, operation, default);
        var claimed = await f.Uploads.TryClaimCleanupAsync(operation with { StorageConfirmed = true }, true, default);
        Assert.NotNull(claimed);
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Uploads.PublishAsync(operation,
            new StoredPdfAsset("https://storage.example.test/late.pdf", operation.PublicId), "hash", default));
        await f.Uploads.RecordCleanupAsync(claimed!, true, default);
        Assert.Equal("Cleaned", f.Firestore.Read(f.OperationPath(operation))!.Fields["Status"].StringValue);
        Assert.Equal(old.PdfUrl, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PdfUrl);
    }

    [Fact]
    public async Task Unknown_storage_outcome_retains_durable_tombstone_for_late_provider_writes()
    {
        var f = new CatalogFixture();
        f.Storage.Setup(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Provider may still complete upload"));
        await Assert.ThrowsAsync<ArtifactDependencyException>(() => f.Upload());
        var due = await f.Uploads.GetDueCleanupAsync(DateTime.UtcNow.AddHours(1), 50, default);
        var operation = Assert.Single(due);
        Assert.False(operation.StorageConfirmed);
        Assert.Equal("CleanupPending", f.Firestore.Read(f.OperationPath(operation))!.Fields["Status"].StringValue);
        f.Storage.Verify(r => r.DeletePdfAsync(operation.PublicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Process_restart_can_reconcile_expired_upload_intent_without_touching_other_scope()
    {
        var f = new CatalogFixture();
        var old = await f.SeedCurrent();
        var service = await f.SeedCurrent("SERVICE");
        var op = PdfUploadOperation.Create(f.WorkspaceId, "PRODUCT", DateTime.UtcNow.AddHours(-1));
        await f.Uploads.BeginAsync(old, op, default);
        var operation = Assert.Single(await f.Uploads.GetDueCleanupAsync(DateTime.UtcNow, 50, default));
        await f.Service.ReconcileUploadAsync(operation, default);
        Assert.Null((await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PendingUploadId);
        Assert.Equal(service.PdfUrl, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "SERVICE", default))!.PdfUrl);
        Assert.Equal("CleanupPending", f.Firestore.Read(f.OperationPath(op))!.Fields["Status"].StringValue);
        f.Storage.Verify(r => r.DeletePdfAsync(op.PublicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Committed_and_replaced_files_are_never_cleaned_by_upload_reconciler()
    {
        var f = new CatalogFixture();
        var initial = CatalogArtifact.Initialize(f.WorkspaceId, "PRODUCT");
        var op = PdfUploadOperation.Create(f.WorkspaceId, "PRODUCT", DateTime.UtcNow);
        await f.Uploads.BeginAsync(initial, op, default);
        await f.Uploads.PublishAsync(op, new("https://storage.example.test/committed.pdf", op.PublicId), "hash", default);
        Assert.Null(await f.Uploads.TryClaimCleanupAsync(op, true, default));
        var artifact = (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!;
        artifact.CompleteUpload("https://storage.example.test/replacement.pdf", "new-hash");
        await f.Artifacts.SaveArtifactAsync(artifact, default);
        Assert.Null(await f.Uploads.TryClaimCleanupAsync(op, true, default));
    }

    [Fact]
    public async Task Referenced_pdf_is_protected_even_if_upload_journal_is_unconfirmed()
    {
        var f = new CatalogFixture();
        var artifact = CatalogArtifact.Initialize(f.WorkspaceId, "PRODUCT");
        var op = PdfUploadOperation.Create(f.WorkspaceId, "PRODUCT", DateTime.UtcNow);
        await f.Uploads.BeginAsync(artifact, op, default);
        artifact.CompleteUpload("https://storage.example.test/referenced.pdf", "hash", op.PublicId);
        await f.Artifacts.SaveArtifactAsync(artifact, default);
        Assert.Null(await f.Uploads.TryClaimCleanupAsync(op, true, default));
    }

    [Fact]
    public async Task Foreign_workspace_or_scope_storage_identity_cannot_be_deleted()
    {
        var f = new CatalogFixture();
        var op = PdfUploadOperation.Create(f.WorkspaceId, "PRODUCT", DateTime.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReconcileUploadAsync(op with { WorkspaceId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReconcileUploadAsync(op with { Scope = "SERVICE" }, default));
        f.Storage.Verify(r => r.DeletePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("GENERATED", "MODERN", "OCEAN", "BALANCED")]
    [InlineData("unknown", "unknown", "unknown", "unknown")]
    public async Task Legacy_firestore_documents_read_without_origin_or_optional_design(string? origin, string? style, string? palette, string? creativity)
    {
        var f = new CatalogFixture();
        var fields = new Dictionary<string, Value>
        {
            ["Id"] = new() { StringValue = Guid.NewGuid().ToString() }, ["Scope"] = new() { StringValue = "PRODUCT" },
            ["SourceHash"] = new() { StringValue = "old-hash" }, ["PdfUrl"] = new() { StringValue = "https://storage.example.test/legacy.pdf" },
            ["Status"] = new() { StringValue = "Current" }
        };
        foreach (var (name, value) in new[] { ("Origin", origin), ("VisualStyle", style), ("Palette", palette), ("Creativity", creativity) })
            if (value != null) fields[name] = new() { StringValue = value };
        f.Firestore.Seed(f.ArtifactPath(), fields);
        var artifact = (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!;
        Assert.Equal(CatalogArtifactOrigin.Generated, artifact.Origin);
        Assert.Equal("https://storage.example.test/legacy.pdf", artifact.PdfUrl);
        Assert.Equal(style == "MODERN", artifact.VisualStyle != null);
        Assert.Null(artifact.PendingUploadId);
    }

    private static bool IsPublication(CommitRequest commit) => commit.Writes.Any(w => w.Update?.Fields.TryGetValue("Status", out var status) == true && status.StringValue == "Committed");

    [Fact]
    public async Task Referenced_pdf_in_sibling_scope_prevents_cleanup_and_releases_upload_lease()
    {
        var f = new CatalogFixture();
        var original = await f.SeedCurrent();
        var op = PdfUploadOperation.Create(f.WorkspaceId, "PRODUCT", DateTime.UtcNow);
        await f.Uploads.BeginAsync(original, op, default);
        var sibling = CatalogArtifact.Initialize(f.WorkspaceId, "SERVICE");
        sibling.CompleteUpload("https://storage.example.test/shared.pdf", "hash", op.PublicId);
        await f.Artifacts.SaveArtifactAsync(sibling, default);
        Assert.Null(await f.Uploads.TryClaimCleanupAsync(op, true, default));
        Assert.Null((await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PendingUploadId);
        Assert.Equal("Committed", f.Firestore.Read(f.OperationPath(op))!.Fields["Status"].StringValue);
    }

    [Fact]
    public async Task Firestore_outage_after_storage_defers_deletion_until_durable_fence_can_commit()
    {
        var f = new CatalogFixture();
        var previous = await f.SeedCurrent();
        f.Storage.Setup(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string name, string folder, CancellationToken _) =>
            {
                f.Firestore.BeforeCommit = _ => new RpcException(new Status(StatusCode.PermissionDenied, "Firestore outage"));
                return new StoredPdfAsset("https://storage.example.test/orphan.pdf", $"{folder}/{name}");
            });
        await Assert.ThrowsAsync<ArtifactDependencyException>(() => f.Upload());
        f.Storage.Verify(r => r.DeletePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(previous.PdfUrl, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PdfUrl);
        var op = Assert.Single(await f.Uploads.GetDueCleanupAsync(DateTime.UtcNow.AddHours(1), 50, default));
        Assert.Equal("Registered", f.Firestore.Read(f.OperationPath(op))!.Fields["Status"].StringValue);
        f.Firestore.BeforeCommit = null;
        var job = f.Firestore.Read(f.OperationPath(op))!;
        job.Fields["NextReconciliationAt"] = new() { TimestampValue = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(-1)) };
        f.Firestore.Seed(f.OperationPath(op), job.Fields);
        await f.Service.ReconcileUploadAsync(op, default);
        f.Storage.Verify(r => r.DeletePdfAsync(op.PublicId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null((await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PendingUploadId);
    }

    [Fact]
    public async Task Failed_external_delete_remains_durable_and_preserves_previous_pdf()
    {
        var f = new CatalogFixture();
        var previous = await f.SeedCurrent();
        f.Storage.Setup(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        f.Storage.Setup(r => r.DeletePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("Storage outage"));
        await Assert.ThrowsAsync<ArtifactDependencyException>(() => f.Upload());
        var op = Assert.Single(await f.Uploads.GetDueCleanupAsync(DateTime.UtcNow.AddHours(1), 50, default));
        Assert.Equal("CleanupPending", f.Firestore.Read(f.OperationPath(op))!.Fields["Status"].StringValue);
        Assert.Equal(previous.PdfUrl, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PdfUrl);
    }

    [Fact]
    public async Task Intent_registration_failure_never_contacts_storage()
    {
        var f = new CatalogFixture();
        f.Firestore.BeforeCommit = _ => new RpcException(new Status(StatusCode.PermissionDenied, "Registration failure"));
        await Assert.ThrowsAsync<RpcException>(() => f.Upload());
        f.Storage.Verify(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Missing_profile_after_storage_cleans_failed_asset_and_preserves_not_found_error()
    {
        var f = new CatalogFixture();
        f.Profiles.Setup(r => r.GetProfileAsync(f.WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync((NexFlow.Application.Features.Business.BusinessProfileDto?)null);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Upload());
        f.Storage.Verify(r => r.DeletePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null((await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PendingUploadId);
    }

    [Fact]
    public async Task Expired_upload_lease_cannot_publish_new_version()
    {
        var f = new CatalogFixture();
        var artifact = await f.SeedCurrent();
        var op = PdfUploadOperation.Create(f.WorkspaceId, "PRODUCT", DateTime.UtcNow.AddHours(-1));
        await f.Uploads.BeginAsync(artifact, op, default);
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Uploads.PublishAsync(op, new("https://storage.example.test/late.pdf", op.PublicId), "hash", default));
        Assert.Equal(artifact.PdfUrl, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PdfUrl);
    }
}
