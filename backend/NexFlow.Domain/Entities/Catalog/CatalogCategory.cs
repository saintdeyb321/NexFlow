using System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities.Catalog;

public class CatalogCategory : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; } // Ideal para ordenar menús o catálogos

    private CatalogCategory() { }

    public static CatalogCategory Create(Guid workspaceId, string name, string? description, int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre de la categoría es obligatorio.");

        return new CatalogCategory
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = name.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            DisplayOrder = displayOrder
        };
    }

    public void Update(string name, string? description, bool isActive, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre de la categoría es obligatorio.");

        Name = name.Trim();
        Description = description?.Trim();
        IsActive = isActive;
        DisplayOrder = displayOrder;
    }
}