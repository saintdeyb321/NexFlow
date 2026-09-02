using System;
using System.Collections.Generic;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities;

public class Product : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Category { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "PEN";
    public bool IsActive { get; private set; }

    // 🔥 Auditoría (Sprint 3.1): Soporte Multi-Sede añadido al catálogo.
    public List<string> AvailableAtLocations { get; private set; } = new();

    private Product() { }

    public static Product Create(Guid workspaceId, string name, string? description, string? category, decimal price, string currency, List<string>? availableAtLocations = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del producto es obligatorio.");
        if (price < 0) throw new DomainException("El precio no puede ser negativo.");

        return new Product
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = name,
            Description = description,
            Category = category,
            Price = price,
            Currency = string.IsNullOrWhiteSpace(currency) ? "PEN" : currency.ToUpperInvariant(),
            IsActive = true,
            AvailableAtLocations = availableAtLocations ?? new List<string>()
        };
    }

    public void Update(string name, string? description, string? category, decimal price, string currency, bool isActive, List<string>? availableAtLocations = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del producto es obligatorio.");
        if (price < 0) throw new DomainException("El precio no puede ser negativo.");

        Name = name;
        Description = description;
        Category = category;
        Price = price;
        Currency = string.IsNullOrWhiteSpace(currency) ? "PEN" : currency.ToUpperInvariant();
        IsActive = isActive;
        AvailableAtLocations = availableAtLocations ?? new List<string>();
    }
}