namespace NexFlow.Application.Features.Business;

public class CatalogItemDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CategoryId { get; set; } = string.Empty;
    public string Type { get; set; } = "PRODUCT"; // "PRODUCT" o "SERVICE"

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceMinorUnits { get; set; }
    public string Currency { get; set; } = "PEN";
    public bool IsActive { get; set; } = true;

    public List<string> AvailableAtLocations { get; set; } = new();

    // 🔥 Campos de Servicios (Vendrán nulos o false si es producto)
    public int? DurationInMinutes { get; set; }
    public bool RequiresReservation { get; set; }

    // Preparación para el Sprint 7
    public string? ImageUrl { get; set; }

    // Flexibilidad NoSQL
    public Dictionary<string, object> Metadata { get; set; } = new();
}