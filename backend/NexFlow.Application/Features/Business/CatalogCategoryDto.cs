using System;

namespace NexFlow.Application.Features.Business;

public class CatalogCategoryDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Scope { get; set; } = "SHARED"; // 🔥 SPRINT 2: Agregado
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
}