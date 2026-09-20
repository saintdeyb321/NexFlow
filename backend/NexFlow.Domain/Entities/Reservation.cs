using System;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;

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
        if (endTime <= startTime)
            throw new DomainException("La fecha de finalización debe ser estrictamente posterior a la fecha de inicio.");

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
            Status = ReservationStatus.Confirmed // Iniciamos en Confirmed
        };
    }

    public void Complete()
    {
        if (Status != ReservationStatus.Confirmed)
            throw new DomainException($"Transición inválida: No se puede completar una reserva en estado {Status}.");

        Status = ReservationStatus.Completed;
    }

    public void Cancel()
    {
        if (Status == ReservationStatus.Completed || Status == ReservationStatus.NoShow || Status == ReservationStatus.Cancelled)
            throw new DomainException($"Transición inválida: No es posible cancelar una reserva en estado {Status}.");

        Status = ReservationStatus.Cancelled;
    }

    public void MarkAsNoShow()
    {
        if (Status != ReservationStatus.Confirmed)
            throw new DomainException($"Transición inválida: Solo las reservas confirmadas pueden marcarse como No-Show.");

        Status = ReservationStatus.NoShow;
    }

    public void Reschedule(DateTime newStartTime, DateTime newEndTime)
    {
        if (Status != ReservationStatus.Confirmed)
            throw new DomainException($"Operación denegada: Solo se pueden reagendar reservas en estado Confirmed (Actual: {Status}).");

        if (newEndTime <= newStartTime)
            throw new DomainException("La nueva fecha de finalización debe ser estrictamente posterior a la de inicio.");

        StartTime = newStartTime;
        EndTime = newEndTime;
    }
}