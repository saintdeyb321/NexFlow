using System;
using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class Reservation : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string LocationId { get; private set; } = null!;
    public string ServiceId { get; private set; } = null!;
    public string CustomerIdentifier { get; private set; } = null!;
    public string CustomerName { get; private set; } = null!;
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }
    public ReservationStatus Status { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    private Reservation() { }

    public static Reservation Create(Guid workspaceId, string locationId, string serviceId, string customerIdentifier, string customerName, DateTime startTime, DateTime endTime)
    {
        return new Reservation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            LocationId = locationId,
            ServiceId = serviceId,
            CustomerIdentifier = customerIdentifier,
            CustomerName = customerName,
            StartTime = startTime,
            EndTime = endTime,
            Status = ReservationStatus.Confirmed
        };
    }

    public void Cancel() => Status = ReservationStatus.Cancelled;

    // 🔥 SPRINT 6: Capacidad de Reagendamiento
    public void Reschedule(DateTime newStartTime, DateTime newEndTime)
    {
        if (Status == ReservationStatus.Cancelled)
            throw new InvalidOperationException("No se puede reagendar una reserva cancelada.");

        StartTime = newStartTime;
        EndTime = newEndTime;
    }
}