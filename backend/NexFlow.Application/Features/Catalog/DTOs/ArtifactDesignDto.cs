using System.ComponentModel.DataAnnotations;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Application.Features.Catalog.DTOs;

public sealed class ArtifactDesignDto
{
    [Required]
    [AllowedValues(nameof(ArtifactVisualStyle.GASTRONOMY), nameof(ArtifactVisualStyle.CLINICAL), nameof(ArtifactVisualStyle.LEGAL),
        nameof(ArtifactVisualStyle.HOSPITALITY), nameof(ArtifactVisualStyle.BEAUTY), nameof(ArtifactVisualStyle.MODERN))]
    public string VisualStyle { get; set; } = string.Empty;

    [Required]
    [AllowedValues(nameof(ArtifactPalette.CLINICAL_BLUE), nameof(ArtifactPalette.WARM_SUNSET), nameof(ArtifactPalette.PREMIUM_DARK),
        nameof(ArtifactPalette.EMERALD), nameof(ArtifactPalette.LAVENDER), nameof(ArtifactPalette.OCEAN))]
    public string Palette { get; set; } = string.Empty;

    [Required]
    [AllowedValues(nameof(ArtifactCreativity.FORMAL), nameof(ArtifactCreativity.BALANCED), nameof(ArtifactCreativity.CREATIVE))]
    public string Creativity { get; set; } = string.Empty;

    public ArtifactDesign ToDesign() => ArtifactDesign.Create(VisualStyle, Palette, Creativity);
}
