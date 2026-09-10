using System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities.Catalog;

public class CatalogArtifact : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string SourceHash { get; private set; } = string.Empty;
    public string? PdfUrl { get; private set; }
    public CatalogArtifactStatus Status { get; private set; }
    public DateTime? LastGeneratedAt { get; private set; }

    private CatalogArtifact() { }

    public static CatalogArtifact Initialize(Guid workspaceId)
    {
        return new CatalogArtifact
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Status = CatalogArtifactStatus.NotGenerated
        };
    }

    public void MarkAsGenerating(string currentHash)
    {
        Status = CatalogArtifactStatus.Generating;
        SourceHash = currentHash;
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