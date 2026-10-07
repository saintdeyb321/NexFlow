namespace NexFlow.Domain.ValueObjects;

public enum ArtifactVisualStyle { GASTRONOMY, CLINICAL, LEGAL, HOSPITALITY, BEAUTY, MODERN }
public enum ArtifactPalette { CLINICAL_BLUE, WARM_SUNSET, PREMIUM_DARK, EMERALD, LAVENDER, OCEAN }
public enum ArtifactCreativity { FORMAL, BALANCED, CREATIVE }

// Presentation preferences for one generation, independent of business facts.
public sealed record ArtifactDesign
{
    public ArtifactVisualStyle VisualStyle { get; }
    public ArtifactPalette Palette { get; }
    public ArtifactCreativity Creativity { get; }

    private ArtifactDesign(ArtifactVisualStyle visualStyle, ArtifactPalette palette, ArtifactCreativity creativity)
        => (VisualStyle, Palette, Creativity) = (visualStyle, palette, creativity);

    public static ArtifactDesign Create(string visualStyle, string palette, string creativity)
        => new(Parse<ArtifactVisualStyle>(visualStyle), Parse<ArtifactPalette>(palette), Parse<ArtifactCreativity>(creativity));

    private static T Parse<T>(string value) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(value, out var parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != value)
            throw new ArgumentException($"Preferencia visual inválida: {typeof(T).Name}.");
        return parsed;
    }
}
