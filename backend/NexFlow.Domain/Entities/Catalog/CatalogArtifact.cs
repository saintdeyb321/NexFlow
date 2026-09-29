using System;
using NexFlow.Domain.Exceptions;

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

    public static CatalogArtifact Restore(Guid id, Guid workspaceId, string scope, string sourceHash, string? pdfUrl, CatalogArtifactStatus status, DateTime? lastGeneratedAt, string? generationId, DateTime? generationStartedAt = null, string? persistenceVersion = null)
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
            PersistenceVersion = persistenceVersion
        };
    }

    public void MarkAsGenerating(string currentHash, string generationId)
    {
        Status = CatalogArtifactStatus.Generating;
        SourceHash = currentHash;
        GenerationId = generationId;
        GenerationStartedAt = DateTime.UtcNow;
        PdfUrl = null;
    }

    public void CompleteGeneration(string pdfUrl)
    {
        if (string.IsNullOrWhiteSpace(pdfUrl)) throw new DomainException("La URL del PDF no puede estar vacía.");

        PdfUrl = pdfUrl;
        Status = CatalogArtifactStatus.Current;
        LastGeneratedAt = DateTime.UtcNow;
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
