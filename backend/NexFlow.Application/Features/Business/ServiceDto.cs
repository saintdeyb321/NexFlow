using System;
using System.Collections.Generic;

namespace NexFlow.Application.Features.Business;

public class ServiceDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }

    // Campos Core (Requeridos por la IA y Reservas)
    public int DurationInMinutes { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PEN";
    public bool RequiresReservation { get; set; }
    public bool IsActive { get; set; } = true;

    // Lista de sedes donde se ofrece (LocationIds)
    public List<string> AvailableAtLocations { get; set; } = new();

    // FLEXIBILIDAD NOSQL: Aquí cada negocio mete sus datos extra
    public Dictionary<string, object> Metadata { get; set; } = new();
}