namespace NexFlow.Application.Features.Shared.DTOs;

public class BusinessCategoryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Scope { get; set; } = "PRODUCT"; // PRODUCT, SERVICE, SHARED
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}