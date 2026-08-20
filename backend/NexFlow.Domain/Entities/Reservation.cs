using System;
using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

// Si tu arquitectura exige que herede de Entity, agrégale el ": Entity"
public class Reservation
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string CustomerIdentifier { get; private set; } = null!;
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }

    public ReservationStatus Status { get; private set; }

    // El método pertenece a la clase y encapsula el cambio de estado
    public void Cancel() => Status = ReservationStatus.Cancelled;

    private Reservation() { }

    // CORREGIDO: Solo recibimos datos puros, sin delegados (Action)
    public static Reservation Create(Guid workspaceId, Guid locationId, Guid serviceId, string customerIdentifier, DateTime startTime, DateTime endTime)
    {
        return new Reservation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            LocationId = locationId,
            ServiceId = serviceId,
            CustomerIdentifier = customerIdentifier,
            StartTime = startTime,
            EndTime = endTime,
            Status = ReservationStatus.Confirmed
        };
    }
}