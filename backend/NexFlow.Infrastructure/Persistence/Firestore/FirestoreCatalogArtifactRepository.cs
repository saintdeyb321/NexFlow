using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Exceptions;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreCatalogArtifactRepository : ICatalogArtifactRepository, ICatalogGenerationUsageRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreCatalogArtifactRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    // ==========================================
    // ARTEFACTOS (PDFs)
    // ==========================================
    public async Task<CatalogArtifact?> GetCurrentArtifactAsync(Guid workspaceId, string scope, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces")
            .Document(workspaceId.ToString())
            .Collection("catalogArtifacts")
            .Document($"current_{scope.ToLowerInvariant()}");

        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists) return null;

        return snapshot.ConvertTo<FirestoreCatalogArtifactDocument>().ToDomain(workspaceId, scope);
    }

    public async Task SaveArtifactAsync(CatalogArtifact artifact, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces")
            .Document(artifact.WorkspaceId.ToString())
            .Collection("catalogArtifacts")
            .Document($"current_{artifact.Scope.ToLowerInvariant()}");

        var data = FirestoreCatalogArtifactDocument.FromDomain(artifact, Guid.NewGuid().ToString("N"));

        await _firestoreDb.RunTransactionAsync(async tx =>
        {
            var current = await tx.GetSnapshotAsync(docRef, cancellationToken);
            var version = current.Exists && current.TryGetValue<string>("PersistenceVersion", out var stored) ? stored : null;
            if (version != artifact.PersistenceVersion) throw new ConcurrencyException("Artifact changed concurrently.");
            tx.Set(docRef, data);
        }, cancellationToken: cancellationToken);
        artifact.PersistenceVersion = data.PersistenceVersion;
    }

    // ==========================================

    // ==========================================
    public async Task<CatalogArtifact> ReserveGenerationAsync(CatalogArtifact artifact, DateTime date, CancellationToken cancellationToken)
    {
        if (artifact.Status != CatalogArtifactStatus.Generating || string.IsNullOrWhiteSpace(artifact.GenerationId))
            throw new ArgumentException("A generating artifact is required.");
        string dateKey = date.ToString("yyyy-MM-dd");
        var workspace = _firestoreDb.Collection("workspaces").Document(artifact.WorkspaceId.ToString());
        var artifactRef = workspace.Collection("catalogArtifacts").Document($"current_{artifact.Scope.ToLowerInvariant()}");
        var docRef = workspace
            .Collection("catalogGenerationUsages")
            .Document(dateKey);
        var data = FirestoreCatalogArtifactDocument.FromDomain(artifact, Guid.NewGuid().ToString("N"));

        return await _firestoreDb.RunTransactionAsync(async transaction =>
        {
            var current = await transaction.GetSnapshotAsync(artifactRef, cancellationToken);
            var currentArtifact = current.Exists ? current.ConvertTo<FirestoreCatalogArtifactDocument>() : null;
            // A retry of an already committed reservation does not spend another daily token.
            if (currentArtifact?.GenerationId == artifact.GenerationId && currentArtifact.SourceHash == artifact.SourceHash)
                return currentArtifact.ToDomain(artifact.WorkspaceId, artifact.Scope);
            if (currentArtifact?.PersistenceVersion != artifact.PersistenceVersion
                || currentArtifact?.Status == nameof(CatalogArtifactStatus.Generating) || currentArtifact?.PendingUploadId != null)
                throw new ConcurrencyException("Artifact changed concurrently.");
            var snapshot = await transaction.GetSnapshotAsync(docRef, cancellationToken);
            int currentCount = snapshot.Exists ? snapshot.GetValue<int>("GenerationCount") : 0;
            var generationIds = snapshot.Exists && snapshot.TryGetValue<List<string>>("GenerationIds", out var ids) ? ids : new List<string>();
            if (generationIds.Contains(artifact.GenerationId)) throw new ConcurrencyException("Reservation is no longer the active generation.");

            if (currentCount >= CatalogGenerationUsage.MaxGenerationsPerDay)
                throw new CatalogQuotaExceededException($"Has alcanzado el límite máximo de {CatalogGenerationUsage.MaxGenerationsPerDay} generaciones de PDF por día.");

            generationIds.Add(artifact.GenerationId);
            var usage = new
            {
                WorkspaceId = artifact.WorkspaceId.ToString(),
                Date = date.ToUniversalTime(),
                GenerationCount = currentCount + 1,
                GenerationIds = generationIds
            };
            // Both writes commit together. A losing artifact CAS cannot consume quota.
            transaction.Set(artifactRef, data);
            transaction.Set(docRef, usage, SetOptions.MergeAll);
            return data.ToDomain(artifact.WorkspaceId, artifact.Scope);
        }, cancellationToken: cancellationToken);
    }
}
