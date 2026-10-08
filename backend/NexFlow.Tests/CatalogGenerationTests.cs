using Google.Cloud.Firestore.V1;
using Grpc.Core;
using Moq;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Exceptions;
using NexFlow.Tests.Fakes;
using System.Text.Json;
using Xunit;

namespace NexFlow.Tests;

public sealed class CatalogGenerationTests
{
    [Fact]
    public async Task Concurrent_requests_accept_one_generation_and_spend_one_token()
    {
        var f = new CatalogFixture();
        f.Firestore.SynchronizeReads(f.ArtifactPath());
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return (Artifact: await f.Generate(), Error: (Exception?)null); }
            catch (Exception e) { return (Artifact: (CatalogArtifact?)null, Error: e); }
        }));
        Assert.Single(attempts, a => a.Artifact != null);
        Assert.IsType<ConcurrencyException>(Assert.Single(attempts, a => a.Error != null).Error);
        Assert.Equal(1, f.Usage);
        Assert.Single(f.Messages);
        Assert.True(f.Firestore.AbortedCommits > 0);
    }

    [Fact]
    public async Task Rejected_conflict_does_not_consume_quota()
    {
        var f = new CatalogFixture();
        await f.Generate();
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Generate());
        Assert.Equal(1, f.Usage);
        Assert.Single(f.Messages);
    }

    [Fact]
    public async Task Daily_limit_is_three_and_rejected_fourth_does_not_claim_artifact()
    {
        var f = new CatalogFixture();
        for (var i = 0; i < 3; i++) { await f.Generate(); await f.FinishGeneration(); }
        await Assert.ThrowsAsync<CatalogQuotaExceededException>(() => f.Generate());
        Assert.Equal(3, f.Usage);
        Assert.Equal(CatalogArtifactStatus.Failed, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
        Assert.Equal(3, f.Messages.Count);
    }

    [Fact]
    public async Task Concurrent_scopes_cannot_exceed_last_shared_daily_token()
    {
        var f = new CatalogFixture();
        for (var i = 0; i < 2; i++) { await f.Generate(); await f.FinishGeneration(); }
        f.Firestore.SynchronizeReads(f.UsagePath);
        var errors = await Task.WhenAll(new[] { "PRODUCT", "SERVICE" }.Select(async scope =>
        {
            try { await f.Generate(scope); return (Exception?)null; } catch (Exception e) { return e; }
        }));
        Assert.Single(errors, e => e == null);
        Assert.IsType<CatalogQuotaExceededException>(Assert.Single(errors, e => e != null));
        Assert.Equal(3, f.Usage);
    }

    [Fact]
    public async Task Retrying_same_reservation_is_idempotent()
    {
        var f = new CatalogFixture();
        var draft = CatalogArtifact.Initialize(f.WorkspaceId, "PRODUCT");
        draft.MarkAsGenerating("hash", "reservation", CatalogFixture.Design);
        var first = await f.Artifacts.ReserveGenerationAsync(draft, DateTime.UtcNow.Date, default);
        var retry = await f.Artifacts.ReserveGenerationAsync(draft, DateTime.UtcNow.Date, default);
        Assert.Equal(first.PersistenceVersion, retry.PersistenceVersion);
        Assert.Equal(1, f.Usage);
    }

    [Fact]
    public async Task Lost_reservation_acknowledgement_can_be_retried_without_second_token()
    {
        var f = new CatalogFixture();
        var draft = CatalogArtifact.Initialize(f.WorkspaceId, "PRODUCT");
        draft.MarkAsGenerating("hash", "reservation", CatalogFixture.Design);
        f.Firestore.AfterCommit = _ => new RpcException(new Status(StatusCode.PermissionDenied, "Lost reservation acknowledgement"));
        await Assert.ThrowsAsync<RpcException>(() => f.Artifacts.ReserveGenerationAsync(draft, DateTime.UtcNow.Date, default));
        Assert.Equal(1, f.Usage);
        f.Firestore.AfterCommit = null;
        Assert.Equal(draft.GenerationId, (await f.Artifacts.ReserveGenerationAsync(draft, DateTime.UtcNow.Date, default)).GenerationId);
        Assert.Equal(1, f.Usage);
    }

    [Fact]
    public async Task Failure_before_firestore_commit_spends_no_quota_and_emits_no_outbox()
    {
        var f = new CatalogFixture();
        f.Firestore.BeforeCommit = _ => new RpcException(new Status(StatusCode.PermissionDenied, "Unavailable fixture"));
        await Assert.ThrowsAsync<RpcException>(() => f.Generate());
        Assert.Equal(0, f.Usage);
        Assert.Null(f.Firestore.Read(f.ArtifactPath()));
        Assert.Empty(f.Messages);
    }

    [Fact]
    public async Task Sql_failure_keeps_reservation_old_pdf_and_quota_for_possible_committed_outbox()
    {
        var f = new CatalogFixture();
        var old = await f.SeedCurrent();
        f.UnitOfWork.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("Lost SQL acknowledgement"));
        await Assert.ThrowsAsync<ArtifactDependencyException>(() => f.Generate());
        var current = (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!;
        Assert.Equal(CatalogArtifactStatus.Generating, current.Status);
        Assert.Equal(old.PdfUrl, current.PdfUrl);
        Assert.Equal(1, f.Usage);
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Generate());
        Assert.Equal(1, f.Usage);
    }

    [Fact]
    public async Task Replacement_requires_confirmation_before_generation_or_upload()
    {
        var f = new CatalogFixture();
        await f.SeedCurrent();
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Generate(replace: false));
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Upload(replace: false));
        Assert.Equal(0, f.Usage);
        f.Storage.Verify(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Previous_pdf_survives_generating_and_failed_states()
    {
        var f = new CatalogFixture();
        var old = await f.SeedCurrent();
        Assert.Equal(old.PdfUrl, (await f.Generate()).PdfUrl);
        await f.FinishGeneration();
        Assert.Equal(old.PdfUrl, (await f.Service.GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PdfUrl);
    }

    [Fact]
    public async Task Product_service_and_workspaces_have_separate_artifacts_and_workspace_quota()
    {
        var f = new CatalogFixture();
        var other = Guid.NewGuid();
        var product = await f.Generate("PRODUCT");
        var service = await f.Generate("SERVICE");
        var otherProduct = await f.Generate("PRODUCT", workspace: other);
        Assert.NotEqual(product.GenerationId, service.GenerationId);
        Assert.Equal(other, otherProduct.WorkspaceId);
        Assert.Equal(2, f.Usage);
        Assert.Equal(1, f.Firestore.Read($"workspaces/{other}/catalogGenerationUsages/{DateTime.UtcNow:yyyy-MM-dd}")!.Fields["GenerationCount"].IntegerValue);
        Assert.Equal(service.GenerationId, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "SERVICE", default))!.GenerationId);
    }

    [Fact]
    public async Task Payload_contains_real_location_resolution_and_only_requested_scope()
    {
        var f = new CatalogFixture();
        f.Locations.Setup(r => r.GetLocationsAsync(f.WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new LocationDto("branch", "Branch", "Real address", "Reference", "https://maps.example.test/branch", true) });
        f.Catalog.Setup(r => r.GetItemsByTypeAsync(f.WorkspaceId, "SERVICE", It.IsAny<CancellationToken>())).ReturnsAsync(new BusinessOfferingDto[] { new ServiceDto { Id = "service", Name = "Consultation", LocationScope = "SPECIFIC", LocationIds = ["branch"] } });
        await f.Generate("SERVICE");
        using var payload = JsonDocument.Parse(Assert.Single(f.Messages).PayloadJson);
        var data = payload.RootElement.GetProperty("Data");
        Assert.Equal("SERVICE", data.GetProperty("Scope").GetString());
        Assert.Equal("Real address", data.GetProperty("Locations")[0].GetProperty("Address").GetString());
        Assert.Equal("branch", data.GetProperty("CatalogData").GetProperty("Items")[0].GetProperty("LocationIds")[0].GetString());
        f.Catalog.Verify(r => r.GetItemsByTypeAsync(f.WorkspaceId, "PRODUCT", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Relevant_business_change_marks_only_matching_artifact_stale()
    {
        var f = new CatalogFixture();
        var product = await f.SeedCurrent();
        await f.SeedCurrent("SERVICE");
        f.Catalog.Setup(r => r.GetItemsByTypeAsync(f.WorkspaceId, "PRODUCT", It.IsAny<CancellationToken>())).ReturnsAsync(new BusinessOfferingDto[] { new ProductDto { Id = "p", Name = "New product" } });
        Assert.Equal(CatalogArtifactStatus.Stale, (await f.Service.GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.Status);
        Assert.Equal(product.PdfUrl, (await f.Artifacts.GetCurrentArtifactAsync(f.WorkspaceId, "PRODUCT", default))!.PdfUrl);
        Assert.Equal(CatalogArtifactStatus.Current, (await f.Service.GetArtifactAsync(f.WorkspaceId, "SERVICE", default))!.Status);
    }

    [Fact]
    public async Task Legacy_daily_usage_without_generation_ids_preserves_existing_count()
    {
        var f = new CatalogFixture();
        f.Firestore.Seed(f.UsagePath, new Dictionary<string, Value> { ["GenerationCount"] = new() { IntegerValue = 2 } });
        await f.Generate();
        await f.FinishGeneration();
        await Assert.ThrowsAsync<CatalogQuotaExceededException>(() => f.Generate("SERVICE"));
        Assert.Equal(3, f.Usage);
    }

    [Fact]
    public async Task Unenqueued_reservation_expires_without_refund_or_losing_previous_pdf()
    {
        var f = new CatalogFixture();
        var previous = await f.SeedCurrent();
        var active = await f.Generate();
        var doc = f.Firestore.Read(f.ArtifactPath())!;
        doc.Fields["GenerationStartedAt"] = new() { TimestampValue = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-2)) };
        f.Firestore.Seed(f.ArtifactPath(), doc.Fields);
        var failed = (await f.Service.GetArtifactAsync(f.WorkspaceId, "PRODUCT", default))!;
        Assert.Equal(CatalogArtifactStatus.Failed, failed.Status);
        Assert.Equal(previous.PdfUrl, failed.PdfUrl);
        Assert.Equal(1, f.Usage);
        Assert.NotEqual(active.GenerationId, (await f.Generate()).GenerationId);
        Assert.Equal(2, f.Usage);
    }
}
