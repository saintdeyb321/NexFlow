using NexFlow.Application.Common;
using NexFlow.Application.DTOs.Reservations;

namespace NexFlow.Application.Engines.Reservation;

public interface IReservationEngine
{
    // Devuelve los bloques de tiempo libres para un día específico
    Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(
        Guid workspaceId,
        Guid locationId,
        Guid serviceId,
        DateTime date,
        CancellationToken cancellationToken);

    // Intenta crear la reserva. Falla si el horario ya se ocupó
    Task<Result<ReservationDto>> CreateReservationAsync(
        Guid workspaceId,
        Guid locationId,
        Guid serviceId,
        string customerIdentifier,
        DateTime dateTime,
        CancellationToken cancellationToken);

    // Cancela una reserva existente
    Task<Result> CancelReservationAsync(
        Guid workspaceId,
        Guid reservationId,
        CancellationToken cancellationToken);
}