using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Exceptions;

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
            .Document($"current_{scope.ToLowerInvariant()}"); // Separamos por PRODUCT, SERVICE o COMBINED

        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists) return null;

        var data = snapshot.ConvertTo<FirestoreArtifact>();

        // 🔥 SPRINT 4: Reconstrucción perfecta sin perder datos
        return CatalogArtifact.Restore(
            Guid.Parse(data.Id),
            workspaceId,
            data.Scope,
            data.SourceHash,
            data.PdfUrl,
            Enum.Parse<CatalogArtifactStatus>(data.Status),
            data.LastGeneratedAt,
            data.GenerationId
        );
    }

    public async Task SaveArtifactAsync(CatalogArtifact artifact, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces")
            .Document(artifact.WorkspaceId.ToString())
            .Collection("catalogArtifacts")
            .Document($"current_{artifact.Scope.ToLowerInvariant()}");

        var data = new FirestoreArtifact
        {
            Id = artifact.Id.ToString(),
            Scope = artifact.Scope,
            SourceHash = artifact.SourceHash,
            PdfUrl = artifact.PdfUrl,
            Status = artifact.Status.ToString(),
            LastGeneratedAt = artifact.LastGeneratedAt,
            GenerationId = artifact.GenerationId
        };

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

    // ==========================================
    // CONTROL ATÓMICO DE LÍMITES DIARIOS (Sprint 4)
    // ==========================================
    public async Task IncrementUsageAtomicallyAsync(Guid workspaceId, DateTime date, CancellationToken cancellationToken)
    {
        string dateKey = date.ToString("yyyy-MM-dd");
        var docRef = _firestoreDb.Collection("workspaces")
            .Document(workspaceId.ToString())
            .Collection("catalogGenerationUsages")
            .Document(dateKey);

        // TRANSACCIÓN ATÓMICA: Imposible que 2 hilos se salten el límite.
        await _firestoreDb.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(docRef, cancellationToken);
            int currentCount = 0;

            if (snapshot.Exists)
            {
                currentCount = snapshot.GetValue<int>("GenerationCount");
            }

            if (currentCount >= CatalogGenerationUsage.MaxGenerationsPerDay)
            {
                throw new DomainException($"Has alcanzado el límite máximo de {CatalogGenerationUsage.MaxGenerationsPerDay} generaciones de PDF por día.");
            }

            var data = new
            {
                WorkspaceId = workspaceId.ToString(),
                Date = date.ToUniversalTime(),
                GenerationCount = currentCount + 1
            };

            transaction.Set(docRef, data, SetOptions.MergeAll);
        });
    }

    [FirestoreData]
    private class FirestoreArtifact
    {
        [FirestoreProperty] public string Id { get; set; } = string.Empty;
        [FirestoreProperty] public string Scope { get; set; } = "COMBINED";
        [FirestoreProperty] public string SourceHash { get; set; } = string.Empty;
        [FirestoreProperty] public string? PdfUrl { get; set; }
        [FirestoreProperty] public string Status { get; set; } = "NotGenerated";
        [FirestoreProperty] public DateTime? LastGeneratedAt { get; set; }
        [FirestoreProperty] public string? GenerationId { get; set; }
    }
}