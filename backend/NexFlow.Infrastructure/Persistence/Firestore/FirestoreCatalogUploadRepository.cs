using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Catalog.Uploads;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public sealed class FirestoreCatalogUploadRepository(FirestoreDb db) : ICatalogUploadRepository
{
    private DocumentReference Artifact(Guid workspaceId, string scope) => db.Collection("workspaces").Document(workspaceId.ToString())
        .Collection("catalogArtifacts").Document($"current_{scope.ToLowerInvariant()}");
    private DocumentReference Operation(PdfUploadOperation operation) => db.Collection("catalogPdfUploads")
        .Document($"{operation.WorkspaceId:N}_{operation.Scope}_{operation.Id}");

    public async Task BeginAsync(CatalogArtifact artifact, PdfUploadOperation operation, CancellationToken ct)
    {
        operation.ValidateStorageIdentity();
        if (artifact.WorkspaceId != operation.WorkspaceId || artifact.Scope != operation.Scope) throw new ArgumentException("Upload scope mismatch.");
        var artifactRef = Artifact(artifact.WorkspaceId, artifact.Scope);
        var operationRef = Operation(operation);
        var claimed = FirestoreCatalogArtifactDocument.FromDomain(artifact).ToDomain(artifact.WorkspaceId, artifact.Scope);
        claimed.BeginUpload(operation.Id, operation.ReconcileAfter);
        var data = FirestoreCatalogArtifactDocument.FromDomain(claimed, Guid.NewGuid().ToString("N"));
        await db.RunTransactionAsync(async tx =>
        {
            var current = await tx.GetSnapshotAsync(artifactRef, ct);
            var existingOperation = await tx.GetSnapshotAsync(operationRef, ct);
            var old = current.Exists ? current.ConvertTo<FirestoreCatalogArtifactDocument>() : null;
            if (existingOperation.Exists || old?.PersistenceVersion != artifact.PersistenceVersion
                || old?.Status == nameof(CatalogArtifactStatus.Generating) || old?.PendingUploadId != null)
                throw new ConcurrencyException("Artifact changed concurrently.");
            tx.Set(artifactRef, data);
            tx.Set(operationRef, UploadDocument.From(operation));
        }, cancellationToken: ct);
        artifact.BeginUpload(operation.Id, operation.ReconcileAfter);
        artifact.PersistenceVersion = data.PersistenceVersion;
    }

    public async Task<CatalogArtifact> PublishAsync(PdfUploadOperation operation, StoredPdfAsset asset, string sourceHash, CancellationToken ct)
    {
        operation.ValidateStorageIdentity();
        if (asset.PublicId != operation.PublicId) throw new ArgumentException("Stored PDF identity mismatch.");
        var artifactRef = Artifact(operation.WorkspaceId, operation.Scope);
        var operationRef = Operation(operation);
        return await db.RunTransactionAsync(async tx =>
        {
            var pending = await tx.GetSnapshotAsync(operationRef, ct);
            var current = await tx.GetSnapshotAsync(artifactRef, ct);
            if (!pending.Exists || !current.Exists) throw new ConcurrencyException("Upload reservation is no longer available.");
            var job = pending.ConvertTo<UploadDocument>();
            job.Validate(operation);
            var artifact = current.ConvertTo<FirestoreCatalogArtifactDocument>().ToDomain(operation.WorkspaceId, operation.Scope);
            if (job.Status == "Committed" && artifact.PdfStoragePublicId == asset.PublicId) return artifact;
            if (job.Status != "Registered" || artifact.PendingUploadId != operation.Id
                || artifact.UploadLeaseExpiresAt is not { } expiresAt || expiresAt <= DateTime.UtcNow
                || artifact.Status == CatalogArtifactStatus.Generating)
                throw new ConcurrencyException("Upload reservation expired or was cancelled.");
            artifact.CompleteUpload(asset.Url, sourceHash, asset.PublicId);
            var data = FirestoreCatalogArtifactDocument.FromDomain(artifact, Guid.NewGuid().ToString("N"));
            tx.Set(artifactRef, data);
            tx.Update(operationRef, new Dictionary<string, object>
            {
                ["Status"] = "Committed", ["StorageConfirmed"] = true, ["PdfUrl"] = asset.Url,
                ["NextReconciliationAt"] = FieldValue.Delete
            });
            return data.ToDomain(operation.WorkspaceId, operation.Scope);
        }, cancellationToken: ct);
    }

    public async Task<PdfUploadOperation?> TryClaimCleanupAsync(PdfUploadOperation operation, bool cancelUpload, CancellationToken ct)
    {
        operation.ValidateStorageIdentity();
        var artifactRef = Artifact(operation.WorkspaceId, operation.Scope);
        var siblingRef = Artifact(operation.WorkspaceId, operation.Scope == "PRODUCT" ? "SERVICE" : "PRODUCT");
        var operationRef = Operation(operation);
        return await db.RunTransactionAsync(async tx =>
        {
            var pending = await tx.GetSnapshotAsync(operationRef, ct);
            var current = await tx.GetSnapshotAsync(artifactRef, ct);
            var sibling = await tx.GetSnapshotAsync(siblingRef, ct);
            if (!pending.Exists) return null;
            var job = pending.ConvertTo<UploadDocument>();
            job.Validate(operation);
            var artifact = current.Exists ? current.ConvertTo<FirestoreCatalogArtifactDocument>().ToDomain(operation.WorkspaceId, operation.Scope) : null;
            var siblingArtifact = sibling.Exists ? sibling.ConvertTo<FirestoreCatalogArtifactDocument>() : null;
            // Committed jobs are never garbage collected, even if a later version replaced them.
            if (job.Status is "Committed" or "Cleaned") return null;
            if (artifact?.PdfStoragePublicId == job.PublicId || (job.PdfUrl != null && artifact?.PdfUrl == job.PdfUrl)
                || (operation.PdfUrl != null && artifact?.PdfUrl == operation.PdfUrl)
                || siblingArtifact?.PdfStoragePublicId == job.PublicId || (job.PdfUrl != null && siblingArtifact?.PdfUrl == job.PdfUrl)
                || (operation.PdfUrl != null && siblingArtifact?.PdfUrl == operation.PdfUrl))
            {
                tx.Update(operationRef, new Dictionary<string, object> { ["Status"] = "Committed", ["NextReconciliationAt"] = FieldValue.Delete });
                if (artifact?.PendingUploadId == operation.Id)
                {
                    artifact.CancelUpload(operation.Id);
                    tx.Set(artifactRef, FirestoreCatalogArtifactDocument.FromDomain(artifact, Guid.NewGuid().ToString("N")));
                }
                return null;
            }
            var now = DateTime.UtcNow;
            if (job.CleanupLeaseUntil > now || (!cancelUpload && job.NextReconciliationAt > now)) return null;
            var token = Guid.NewGuid().ToString("N");
            // Cancellation and the fence commit before any external deletion. Publish requires Registered.
            tx.Update(operationRef, new Dictionary<string, object>
            {
                ["Status"] = "CleanupPending", ["StorageConfirmed"] = job.StorageConfirmed || operation.StorageConfirmed,
                ["PdfUrl"] = (object?)operation.PdfUrl ?? job.PdfUrl ?? string.Empty,
                ["CleanupToken"] = token, ["CleanupLeaseUntil"] = now.AddMinutes(2), ["NextReconciliationAt"] = now.AddMinutes(2)
            });
            if (artifact?.PendingUploadId == operation.Id)
            {
                artifact.CancelUpload(operation.Id);
                tx.Set(artifactRef, FirestoreCatalogArtifactDocument.FromDomain(artifact, Guid.NewGuid().ToString("N")));
            }
            return operation with { StorageConfirmed = job.StorageConfirmed || operation.StorageConfirmed, CleanupToken = token };
        }, cancellationToken: ct);
    }

    public async Task RecordCleanupAsync(PdfUploadOperation operation, bool deleted, CancellationToken ct)
    {
        var operationRef = Operation(operation);
        await db.RunTransactionAsync(async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(operationRef, ct);
            if (!snapshot.Exists) return;
            var job = snapshot.ConvertTo<UploadDocument>();
            job.Validate(operation);
            if (job.Status != "CleanupPending" || job.CleanupToken != operation.CleanupToken) return;
            // Unknown upload outcomes keep a tombstone: a delayed provider write may appear after a delete/not_found.
            var complete = deleted && job.StorageConfirmed;
            tx.Update(operationRef, new Dictionary<string, object>
            {
                ["Status"] = complete ? "Cleaned" : "CleanupPending",
                ["NextReconciliationAt"] = complete ? FieldValue.Delete : DateTime.UtcNow.AddMinutes(deleted ? 30 : 2),
                ["CleanupToken"] = FieldValue.Delete, ["CleanupLeaseUntil"] = FieldValue.Delete
            });
        }, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<PdfUploadOperation>> GetDueCleanupAsync(DateTime now, int limit, CancellationToken ct)
    {
        // One indexed timestamp field; no composite index or collection-group index is needed.
        var snapshot = await db.Collection("catalogPdfUploads").WhereLessThanOrEqualTo("NextReconciliationAt", now).Limit(limit).GetSnapshotAsync(ct);
        return snapshot.Documents.Select(doc => doc.ConvertTo<UploadDocument>().ToOperation()).ToArray();
    }

    [FirestoreData]
    private sealed class UploadDocument
    {
        [FirestoreProperty] public string Id { get; set; } = string.Empty;
        [FirestoreProperty] public string WorkspaceId { get; set; } = string.Empty;
        [FirestoreProperty] public string Scope { get; set; } = string.Empty;
        [FirestoreProperty] public string PublicId { get; set; } = string.Empty;
        [FirestoreProperty] public string Status { get; set; } = "Registered";
        [FirestoreProperty] public DateTime? NextReconciliationAt { get; set; }
        [FirestoreProperty] public bool StorageConfirmed { get; set; }
        [FirestoreProperty] public string? PdfUrl { get; set; }
        [FirestoreProperty] public string? CleanupToken { get; set; }
        [FirestoreProperty] public DateTime? CleanupLeaseUntil { get; set; }
        public static UploadDocument From(PdfUploadOperation operation) => new()
        {
            Id = operation.Id, WorkspaceId = operation.WorkspaceId.ToString(), Scope = operation.Scope,
            PublicId = operation.PublicId, NextReconciliationAt = operation.ReconcileAfter
        };
        public PdfUploadOperation ToOperation() => new(Id, Guid.Parse(WorkspaceId), Scope, PublicId,
            NextReconciliationAt ?? DateTime.MaxValue, StorageConfirmed, PdfUrl, CleanupToken);
        public void Validate(PdfUploadOperation expected)
        {
            if (Id != expected.Id || WorkspaceId != expected.WorkspaceId.ToString() || Scope != expected.Scope || PublicId != expected.PublicId)
                throw new ConcurrencyException("Upload identity mismatch.");
            expected.ValidateStorageIdentity();
        }
    }
}
