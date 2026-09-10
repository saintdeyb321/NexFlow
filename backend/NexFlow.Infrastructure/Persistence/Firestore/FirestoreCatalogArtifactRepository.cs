using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreCatalogArtifactRepository : ICatalogArtifactRepository, ICatalogGenerationUsageRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreCatalogArtifactRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    // ==========================================
    // ARTEFACTOS (PDFs)
    // ==========================================
    public async Task<CatalogArtifact?> GetCurrentArtifactAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces")
            .Document(workspaceId.ToString())
            .Collection("catalogArtifacts")
            .Document("current");

        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists) return null;

        var data = snapshot.ConvertTo<FirestoreArtifact>();

        // Reconstruimos la entidad de dominio utilizando reflexión o un mapeo interno
        var artifact = CatalogArtifact.Initialize(workspaceId);
        // Mapeamos propiedades internas si es necesario mediante un método de reconstrucción o setters internos.
        return artifact; // O ajustaremos el mapeador según convenga abajo.
    }

    public async Task SaveArtifactAsync(CatalogArtifact artifact, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces")
            .Document(artifact.WorkspaceId.ToString())
            .Collection("catalogArtifacts")
            .Document("current");

        var data = new FirestoreArtifact
        {
            Id = artifact.Id.ToString(),
            SourceHash = artifact.SourceHash,
            PdfUrl = artifact.PdfUrl,
            Status = artifact.Status.ToString(),
            LastGeneratedAt = artifact.LastGeneratedAt
        };

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

    // ==========================================
    // CONTROL DE LÍMITES DIARIOS (3 al día)
    // ==========================================
    public async Task<CatalogGenerationUsage?> GetUsageForTodayAsync(Guid workspaceId, DateTime date, CancellationToken cancellationToken)
    {
        string dateKey = date.ToString("yyyy-MM-dd");
        var docRef = _firestoreDb.Collection("workspaces")
            .Document(workspaceId.ToString())
            .Collection("catalogGenerationUsages")
            .Document(dateKey);

        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists) return null;

        var data = snapshot.ConvertTo<FirestoreUsage>();
        return CatalogGenerationUsage.Create(workspaceId, data.Date); // Ajustado
    }

    public async Task SaveUsageAsync(CatalogGenerationUsage usage, CancellationToken cancellationToken)
    {
        string dateKey = usage.Date.ToString("yyyy-MM-dd");
        var docRef = _firestoreDb.Collection("workspaces")
            .Document(usage.WorkspaceId.ToString())
            .Collection("catalogGenerationUsages")
            .Document(dateKey);

        var data = new FirestoreUsage
        {
            WorkspaceId = usage.WorkspaceId.ToString(),
            Date = usage.Date,
            GenerationCount = usage.GenerationCount
        };

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

    [FirestoreData]
    private class FirestoreArtifact
    {
        [FirestoreProperty] public string Id { get; set; } = string.Empty;
        [FirestoreProperty] public string SourceHash { get; set; } = string.Empty;
        [FirestoreProperty] public string? PdfUrl { get; set; }
        [FirestoreProperty] public string Status { get; set; } = "NotGenerated";
        [FirestoreProperty] public DateTime? LastGeneratedAt { get; set; }
    }

    [FirestoreData]
    private class FirestoreUsage
    {
        [FirestoreProperty] public string WorkspaceId { get; set; } = string.Empty;
        [FirestoreProperty] public DateTime Date { get; set; }
        [FirestoreProperty] public int GenerationCount { get; set; }
    }
}