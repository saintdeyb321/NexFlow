using System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities;

public class Service : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Category { get; private set; }
    public int DurationInMinutes { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "PEN";
    public bool IsActive { get; private set; }
    public bool RequiresReservation { get; private set; }

    private Service() { }

    public static Service Create(Guid workspaceId, string name, string? description, string? category, int duration, decimal price, string currency, bool requiresReservation)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del servicio es obligatorio.");
        if (duration <= 0) throw new DomainException("La duración debe ser mayor a 0.");
        if (price < 0) throw new DomainException("El precio no puede ser negativo.");

        return new Service
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = name,
            Description = description,
            Category = category,
            DurationInMinutes = duration,
            Price = price,
            Currency = string.IsNullOrWhiteSpace(currency) ? "PEN" : currency.ToUpperInvariant(),
            IsActive = true,
            RequiresReservation = requiresReservation
        };
    }

    public void Update(string name, string? description, string? category, int duration, decimal price, string currency, bool isActive, bool requiresReservation)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("El nombre del servicio es obligatorio.");
        if (duration <= 0) throw new DomainException("La duración debe ser mayor a 0.");
        if (price < 0) throw new DomainException("El precio no puede ser negativo.");

        Name = name;
        Description = description;
        Category = category;
        DurationInMinutes = duration;
        Price = price;
        Currency = currency.ToUpperInvariant();
        IsActive = isActive;
        RequiresReservation = requiresReservation;
    }
}