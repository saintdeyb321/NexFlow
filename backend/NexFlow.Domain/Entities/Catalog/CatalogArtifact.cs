using System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities.Catalog;

public class CatalogArtifact : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Scope { get; private set; } = "COMBINED"; // PRODUCT, SERVICE, COMBINED
    public string SourceHash { get; private set; } = string.Empty;
    public string? PdfUrl { get; private set; }
    public CatalogArtifactStatus Status { get; private set; }
    public DateTime? LastGeneratedAt { get; private set; }
    public string? GenerationId { get; private set; }

    private CatalogArtifact() { }

    public static CatalogArtifact Initialize(Guid workspaceId, string scope)
    {
        return new CatalogArtifact
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Scope = scope.ToUpperInvariant(),
            Status = CatalogArtifactStatus.NotGenerated
        };
    }

    // 🔥 SPRINT 4: Método para reconstruir la entidad desde BD sin alterar su estado
    public static CatalogArtifact Restore(Guid id, Guid workspaceId, string scope, string sourceHash, string? pdfUrl, CatalogArtifactStatus status, DateTime? lastGeneratedAt, string? generationId)
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
            GenerationId = generationId
        };
    }

    public void MarkAsGenerating(string currentHash, string generationId)
    {
        Status = CatalogArtifactStatus.Generating;
        SourceHash = currentHash;
        GenerationId = generationId;
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