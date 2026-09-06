using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions;

public interface IReservationRepository
{
    void Add(Reservation reservation);

    Task<Reservation?> GetActiveReservationByPhoneAsync(Guid workspaceId, string customerIdentifier, CancellationToken cancellationToken);

    Task<Reservation?> GetByIdAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken);
    Task<IEnumerable<Reservation>> GetReservationsForDateAsync(Guid workspaceId, string locationId, DateTime date, CancellationToken cancellationToken);
    Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, string locationId, DateTime startTimeUtc, DateTime endTimeUtc, Guid? excludeReservationId = null, CancellationToken cancellationToken = default);
}