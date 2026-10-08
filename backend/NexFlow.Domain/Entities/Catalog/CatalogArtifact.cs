using System;
using NexFlow.Domain.Exceptions;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Domain.Entities.Catalog;

public class CatalogArtifact : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Scope { get; private set; } = "PRODUCT";
    public string SourceHash { get; private set; } = string.Empty;
    public string? PdfUrl { get; private set; }
    public CatalogArtifactStatus Status { get; private set; }
    public DateTime? LastGeneratedAt { get; private set; }
    public string? GenerationId { get; private set; }
    public DateTime? GenerationStartedAt { get; private set; }
    public string? PersistenceVersion { get; set; }
    public CatalogArtifactOrigin Origin { get; private set; } = CatalogArtifactOrigin.Generated;
    public ArtifactVisualStyle? VisualStyle { get; private set; }
    public ArtifactPalette? Palette { get; private set; }
    public ArtifactCreativity? Creativity { get; private set; }
    public string? PdfStoragePublicId { get; private set; }
    public string? PendingUploadId { get; private set; }
    public DateTime? UploadLeaseExpiresAt { get; private set; }

    private CatalogArtifact() { }

    public static CatalogArtifact Initialize(Guid workspaceId, string scope)
    {
        if (scope.ToUpperInvariant() is not ("PRODUCT" or "SERVICE")) throw new DomainException("Scope inválido.");
        return new CatalogArtifact
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Scope = scope.ToUpperInvariant(),
            Status = CatalogArtifactStatus.NotGenerated
        };
    }

    public static CatalogArtifact Restore(Guid id, Guid workspaceId, string scope, string sourceHash, string? pdfUrl, CatalogArtifactStatus status, DateTime? lastGeneratedAt, string? generationId, DateTime? generationStartedAt = null, string? persistenceVersion = null,
        CatalogArtifactOrigin origin = CatalogArtifactOrigin.Generated, ArtifactVisualStyle? visualStyle = null, ArtifactPalette? palette = null, ArtifactCreativity? creativity = null,
        string? pdfStoragePublicId = null, string? pendingUploadId = null, DateTime? uploadLeaseExpiresAt = null)
    {
        return new CatalogArtifact
        {
            Id = id,
            WorkspaceId = workspaceId,
            Scope = scope,
            SourceHash = sourceHash,
            PdfUrl = pdfUrl,
            Status = status,
            LastGeneratedAt = lastGeneratedAt,
            GenerationId = generationId,
            GenerationStartedAt = generationStartedAt,
            PersistenceVersion = persistenceVersion,
            Origin = origin,
            VisualStyle = visualStyle,
            Palette = palette,
            Creativity = creativity,
            PdfStoragePublicId = pdfStoragePublicId,
            PendingUploadId = pendingUploadId,
            UploadLeaseExpiresAt = uploadLeaseExpiresAt
        };
    }

    public void EnsureCanReplace(bool replaceCurrent)
    {
        if (PendingUploadId != null) throw new ConcurrencyException("Ya existe una subida de PDF en curso.");
        if (Status == CatalogArtifactStatus.Generating)
            throw new ConcurrencyException("Estamos creando tu nuevo folleto. Espera antes de generar o subir otra versión.");
        if (!replaceCurrent && (Status is CatalogArtifactStatus.Current or CatalogArtifactStatus.Stale || !string.IsNullOrWhiteSpace(PdfUrl)))
            throw new ConcurrencyException("Confirma explícitamente el reemplazo del folleto actual.");
    }

    public void MarkAsGenerating(string currentHash, string generationId, ArtifactDesign design)
    {
        if (Status == CatalogArtifactStatus.Generating || PendingUploadId != null) throw new ConcurrencyException("Ya existe una operación de folleto en curso.");
        Status = CatalogArtifactStatus.Generating;
        SourceHash = currentHash;
        GenerationId = generationId;
        GenerationStartedAt = DateTime.UtcNow;
        VisualStyle = design.VisualStyle;
        Palette = design.Palette;
        Creativity = design.Creativity;
    }

    public void CompleteGeneration(string pdfUrl)
    {
        if (string.IsNullOrWhiteSpace(pdfUrl)) throw new DomainException("La URL del PDF no puede estar vacía.");

        PdfUrl = pdfUrl;
        Status = CatalogArtifactStatus.Current;
        LastGeneratedAt = DateTime.UtcNow;
        Origin = CatalogArtifactOrigin.Generated;
        PdfStoragePublicId = null;
    }

    public void BeginUpload(string operationId, DateTime expiresAt)
    {
        if (Status == CatalogArtifactStatus.Generating || PendingUploadId != null) throw new ConcurrencyException("Ya existe una operación de folleto en curso.");
        PendingUploadId = operationId;
        UploadLeaseExpiresAt = expiresAt;
    }

    public void CancelUpload(string operationId)
    {
        if (PendingUploadId != operationId) return;
        PendingUploadId = null;
        UploadLeaseExpiresAt = null;
    }

    public void CompleteUpload(string pdfUrl, string currentHash, string? storagePublicId = null)
    {
        if (Status == CatalogArtifactStatus.Generating) throw new ConcurrencyException("Ya existe una generación en curso.");
        if (string.IsNullOrWhiteSpace(pdfUrl)) throw new DomainException("La URL del PDF no puede estar vacía.");
        PdfUrl = pdfUrl;
        SourceHash = currentHash;
        Status = CatalogArtifactStatus.Current;
        LastGeneratedAt = DateTime.UtcNow;
        Origin = CatalogArtifactOrigin.Uploaded;
        PdfStoragePublicId = storagePublicId;
        PendingUploadId = null;
        UploadLeaseExpiresAt = null;
        VisualStyle = null;
        Palette = null;
        Creativity = null;
        GenerationId = null;
        GenerationStartedAt = null;
    }

    public void MarkAsStale()
    {
        if (Status == CatalogArtifactStatus.Current)
        {
            Status = CatalogArtifactStatus.Stale;
        }
    }

    public void MarkAsFailed()
    {
        Status = CatalogArtifactStatus.Failed;
    }
}
