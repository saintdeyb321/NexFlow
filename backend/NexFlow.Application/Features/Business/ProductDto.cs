namespace NexFlow.Application.Features.Business;

public class ProductDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PEN";
    public bool IsActive { get; set; } = true;

    // Metadatos flexibles (Ej: URL de imagen, variaciones de tamaño)
    public Dictionary<string, object> Metadata { get; set; } = new();
}