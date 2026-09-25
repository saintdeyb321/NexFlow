namespace NexFlow.Application.Features.Shared.DTOs;

public abstract class BusinessOfferingDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CategoryId { get; set; } = string.Empty;
    public string Type { get; protected set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceMinorUnits { get; set; }
    public string Currency { get; set; } = "PEN";
    public bool IsActive { get; set; } = true;
    public string LocationScope { get; set; } = "ALL";
    public List<string> LocationIds { get; set; } = new();
    public string? ImageUrl { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}