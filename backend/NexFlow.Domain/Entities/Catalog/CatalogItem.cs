using System;
using System.Collections.Generic;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities.Catalog;

public class CatalogItem : Entity
{
    public Guid WorkspaceId { get; private set; }
    public Guid CategoryId { get; private set; } // 🔥 Ahora es una relación real, no un string
    public CatalogItemType Type { get; private set; }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "PEN";
    public bool IsActive { get; private set; }
    public List<string> AvailableAtLocations { get; private set; } = new();

    // Campos exclusivos de Servicios (Nulos por defecto para Productos)
    public int? DurationInMinutes { get; private set; }
    public bool RequiresReservation { get; private set; }

    // Preparación para el Sprint 7 (Multimedia)
    public string? ImageUrl { get; private set; }

    private CatalogItem() { }

    // Factory Method para PRODUCTOS
    public static CatalogItem CreateProduct(Guid workspaceId, Guid categoryId, string name, string? description, decimal price, string currency, List<string>? availableAtLocations = null, string? imageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del producto es obligatorio.");
        if (price < 0) throw new DomainException("El precio no puede ser negativo.");
        if (categoryId == Guid.Empty) throw new DomainException("La categoría es obligatoria.");

        return new CatalogItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CategoryId = categoryId,
            Type = CatalogItemType.Product,
            Name = name.Trim(),
            Description = description?.Trim(),
            Price = price,
            Currency = string.IsNullOrWhiteSpace(currency) ? "PEN" : currency.ToUpperInvariant(),
            IsActive = true,
            AvailableAtLocations = availableAtLocations ?? new List<string>(),
            ImageUrl = imageUrl
        };
    }

    // Factory Method para SERVICIOS
    public static CatalogItem CreateService(Guid workspaceId, Guid categoryId, string name, string? description, decimal price, string currency, int duration, bool requiresReservation, List<string>? availableAtLocations = null, string? imageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del servicio es obligatorio.");
        if (price < 0) throw new DomainException("El precio no puede ser negativo.");
        if (categoryId == Guid.Empty) throw new DomainException("La categoría es obligatoria.");
        if (duration <= 0) throw new DomainException("La duración del servicio debe ser mayor a 0.");

        return new CatalogItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CategoryId = categoryId,
            Type = CatalogItemType.Service,
            Name = name.Trim(),
            Description = description?.Trim(),
            Price = price,
            Currency = string.IsNullOrWhiteSpace(currency) ? "PEN" : currency.ToUpperInvariant(),
            IsActive = true,
            AvailableAtLocations = availableAtLocations ?? new List<string>(),
            DurationInMinutes = duration,
            RequiresReservation = requiresReservation,
            ImageUrl = imageUrl
        };
    }

    public void Update(Guid categoryId, string name, string? description, decimal price, string currency, bool isActive, List<string>? availableAtLocations = null, string? imageUrl = null, int? duration = null, bool? requiresReservation = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del ítem es obligatorio.");
        if (price < 0) throw new DomainException("El precio no puede ser negativo.");
        if (categoryId == Guid.Empty) throw new DomainException("La categoría es obligatoria.");
        if (Type == CatalogItemType.Service && (duration == null || duration <= 0)) throw new DomainException("La duración del servicio debe ser mayor a 0.");

        CategoryId = categoryId;
        Name = name.Trim();
        Description = description?.Trim();
        Price = price;
        Currency = string.IsNullOrWhiteSpace(currency) ? "PEN" : currency.ToUpperInvariant();
        IsActive = isActive;
        AvailableAtLocations = availableAtLocations ?? new List<string>();

        if (imageUrl != null) ImageUrl = imageUrl;

        if (Type == CatalogItemType.Service)
        {
            DurationInMinutes = duration;
            RequiresReservation = requiresReservation ?? false;
        }
    }
}