using Google.Cloud.Firestore;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Exceptions;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Infrastructure.Persistence.Firestore;

[FirestoreData]
internal sealed class FirestoreCatalogArtifactDocument
{
    [FirestoreProperty] public string Id { get; set; } = string.Empty;
    [FirestoreProperty] public string Scope { get; set; } = "PRODUCT";
    [FirestoreProperty] public string SourceHash { get; set; } = string.Empty;
    [FirestoreProperty] public string? PdfUrl { get; set; }
    [FirestoreProperty] public string Status { get; set; } = "NotGenerated";
    [FirestoreProperty] public DateTime? LastGeneratedAt { get; set; }
    [FirestoreProperty] public string? GenerationId { get; set; }
    [FirestoreProperty] public DateTime? GenerationStartedAt { get; set; }
    [FirestoreProperty] public string? PersistenceVersion { get; set; }
    [FirestoreProperty] public string? Origin { get; set; }
    [FirestoreProperty] public string? VisualStyle { get; set; }
    [FirestoreProperty] public string? Palette { get; set; }
    [FirestoreProperty] public string? Creativity { get; set; }
    [FirestoreProperty] public string? PdfStoragePublicId { get; set; }
    [FirestoreProperty] public string? PendingUploadId { get; set; }
    [FirestoreProperty] public DateTime? UploadLeaseExpiresAt { get; set; }

    public CatalogArtifact ToDomain(Guid workspaceId, string scope)
    {
        if (Scope != scope) throw new ConcurrencyException("Artifact scope mismatch.");
        return CatalogArtifact.Restore(Guid.Parse(Id), workspaceId, Scope, SourceHash, PdfUrl, Enum.Parse<CatalogArtifactStatus>(Status),
            LastGeneratedAt, GenerationId, GenerationStartedAt, PersistenceVersion,
            Parse<CatalogArtifactOrigin>(Origin) ?? CatalogArtifactOrigin.Generated,
            Parse<ArtifactVisualStyle>(VisualStyle), Parse<ArtifactPalette>(Palette), Parse<ArtifactCreativity>(Creativity),
            PdfStoragePublicId, PendingUploadId, UploadLeaseExpiresAt);
    }

    public static FirestoreCatalogArtifactDocument FromDomain(CatalogArtifact artifact, string? version = null) => new()
    {
        Id = artifact.Id.ToString(), Scope = artifact.Scope, SourceHash = artifact.SourceHash, PdfUrl = artifact.PdfUrl,
        Status = artifact.Status.ToString(), LastGeneratedAt = artifact.LastGeneratedAt, GenerationId = artifact.GenerationId,
        GenerationStartedAt = artifact.GenerationStartedAt, PersistenceVersion = version ?? artifact.PersistenceVersion,
        Origin = artifact.Origin.ToString().ToUpperInvariant(), VisualStyle = artifact.VisualStyle?.ToString(),
        Palette = artifact.Palette?.ToString(), Creativity = artifact.Creativity?.ToString(), PdfStoragePublicId = artifact.PdfStoragePublicId,
        PendingUploadId = artifact.PendingUploadId, UploadLeaseExpiresAt = artifact.UploadLeaseExpiresAt
    };

    private static T? Parse<T>(string? value) where T : struct, Enum
        => Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
}
