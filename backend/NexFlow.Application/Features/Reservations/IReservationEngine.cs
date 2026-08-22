using NexFlow.Application.Common;

namespace NexFlow.Application.Features.Reservations;

public interface IReservationEngine
{
    // Devuelve los bloques de tiempo libres para un día específico
    Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(
        Guid workspaceId,
        string locationId,
        string serviceId,
        DateTime date,
        CancellationToken cancellationToken);

    // Intenta crear la reserva. Falla si el horario ya se ocupó
    Task<Result<ReservationDto>> CreateReservationAsync(
        Guid workspaceId,
        string locationId,
        string serviceId,
        string customerIdentifier,
        string customerName,    
        DateTime dateTime,
        CancellationToken cancellationToken);


    // Cancela una reserva existente
    Task<Result> CancelReservationAsync(
        Guid workspaceId,
        Guid reservationId,
        CancellationToken cancellationToken);
}