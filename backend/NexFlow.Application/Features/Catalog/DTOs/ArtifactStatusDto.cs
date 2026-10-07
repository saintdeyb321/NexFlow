using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Features.Catalog.DTOs;

public sealed record ArtifactStatusDto(string Status, string? PdfUrl, DateTime? LastGeneratedAt,
    string Origin, string? VisualStyle, string? Palette, string? Creativity)
{
    public static ArtifactStatusDto From(CatalogArtifact? artifact) => new(
        artifact == null || artifact.Status == CatalogArtifactStatus.NotGenerated ? "NOT_GENERATED" : artifact.Status.ToString().ToUpperInvariant(),
        artifact?.PdfUrl, artifact?.LastGeneratedAt,
        (artifact?.Origin ?? CatalogArtifactOrigin.Generated).ToString().ToUpperInvariant(),
        artifact?.VisualStyle?.ToString(), artifact?.Palette?.ToString(), artifact?.Creativity?.ToString());
}
