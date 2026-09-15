using System;
using System.Collections.Generic;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities.Catalog;

public class CatalogItem : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string CategoryId { get; private set; } = string.Empty;
    public CatalogItemType Type { get; private set; }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public long PriceMinorUnits { get; private set; }
    public string Currency { get; private set; } = "PEN";
    public bool IsActive { get; private set; }

    // 🔥 SPRINT 02: Dimensión real de sede en el Dominio
    public LocationScope LocationScope { get; private set; } = LocationScope.All;
    public List<string> LocationIds { get; private set; } = new();

    public int? DurationInMinutes { get; private set; }
    public bool RequiresReservation { get; private set; }
    public string? ImageUrl { get; private set; }

    private CatalogItem() { }

    // 🔥 SPRINT 02: ÚNICA FUENTE DE VERDAD PARA DISPONIBILIDAD
    public bool IsAvailableAtLocation(string locationId)
    {
        if (LocationScope == LocationScope.All) return true;
        if (string.IsNullOrWhiteSpace(locationId)) return false;
        return LocationIds.Contains(locationId);
    }

    public static CatalogItem CreateProduct(Guid workspaceId, string categoryId, string name, string? description, long priceMinorUnits, string currency, LocationScope locationScope = LocationScope.All, List<string>? locationIds = null, string? imageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del producto es obligatorio.");
        if (priceMinorUnits < 0) throw new DomainException("El precio no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(categoryId)) throw new DomainException("La categoría es obligatoria.");

        return new CatalogItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CategoryId = categoryId,
            Type = CatalogItemType.Product,
            Name = name.Trim(),
            Description = description?.Trim(),
            PriceMinorUnits = priceMinorUnits,
            Currency = string.IsNullOrWhiteSpace(currency) ? "PEN" : currency.ToUpperInvariant(),
            IsActive = true,
            LocationScope = locationScope,
            LocationIds = locationIds ?? new List<string>(),
            ImageUrl = imageUrl
        };
    }

    public static CatalogItem CreateService(Guid workspaceId, string categoryId, string name, string? description, long priceMinorUnits, string currency, int duration, bool requiresReservation, LocationScope locationScope = LocationScope.All, List<string>? locationIds = null, string? imageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del servicio es obligatorio.");
        if (priceMinorUnits < 0) throw new DomainException("El precio no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(categoryId)) throw new DomainException("La categoría es obligatoria.");
        if (duration <= 0) throw new DomainException("La duración del servicio debe ser mayor a 0.");

        return new CatalogItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CategoryId = categoryId,
            Type = CatalogItemType.Service,
            Name = name.Trim(),
            Description = description?.Trim(),
            PriceMinorUnits = priceMinorUnits,
            Currency = string.IsNullOrWhiteSpace(currency) ? "PEN" : currency.ToUpperInvariant(),
            IsActive = true,
            LocationScope = locationScope,
            LocationIds = locationIds ?? new List<string>(),
            DurationInMinutes = duration,
            RequiresReservation = requiresReservation,
            ImageUrl = imageUrl
        };
    }

    public void Update(string categoryId, string name, string? description, long priceMinorUnits, string currency, bool isActive, LocationScope locationScope, List<string>? locationIds = null, string? imageUrl = null, int? duration = null, bool? requiresReservation = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del ítem es obligatorio.");
        if (priceMinorUnits < 0) throw new DomainException("El precio no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(categoryId)) throw new DomainException("La categoría es obligatoria.");
        if (Type == CatalogItemType.Service && (duration == null || duration <= 0)) throw new DomainException("La duración del servicio debe ser mayor a 0.");

        CategoryId = categoryId;
        Name = name.Trim();
        Description = description?.Trim();
        PriceMinorUnits = priceMinorUnits;
        Currency = string.IsNullOrWhiteSpace(currency) ? "PEN" : currency.ToUpperInvariant();
        IsActive = isActive;
        LocationScope = locationScope;
        LocationIds = locationIds ?? new List<string>();

        if (imageUrl != null) ImageUrl = imageUrl;

        if (Type == CatalogItemType.Service)
        {
            DurationInMinutes = duration;
            RequiresReservation = requiresReservation ?? false;
        }
    }
}