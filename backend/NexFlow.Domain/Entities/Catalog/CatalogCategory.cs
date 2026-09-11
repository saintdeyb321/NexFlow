using System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities.Catalog;

public class CatalogCategory : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Scope { get; private set; } = "SHARED"; // 🔥 SPRINT 2: "PRODUCT", "SERVICE" o "SHARED"
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }

    private CatalogCategory() { }

    public static CatalogCategory Create(Guid workspaceId, string name, string? description, string scope = "SHARED", int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre de la categoría es obligatorio.");

        var validScope = scope.ToUpperInvariant();
        if (validScope != "PRODUCT" && validScope != "SERVICE" && validScope != "SHARED")
            throw new DomainException("El ámbito (Scope) de la categoría debe ser PRODUCT, SERVICE o SHARED.");

        return new CatalogCategory
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = name.Trim(),
            Description = description?.Trim(),
            Scope = validScope,
            IsActive = true,
            DisplayOrder = displayOrder
        };
    }

    public void Update(string name, string? description, string scope, bool isActive, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre de la categoría es obligatorio.");

        var validScope = scope.ToUpperInvariant();
        if (validScope != "PRODUCT" && validScope != "SERVICE" && validScope != "SHARED")
            throw new DomainException("El ámbito (Scope) de la categoría debe ser PRODUCT, SERVICE o SHARED.");

        Name = name.Trim();
        Description = description?.Trim();
        Scope = validScope;
        IsActive = isActive;
        DisplayOrder = displayOrder;
    }
}