using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions;

public interface IReservationRepository
{
    Task<(int Today, int Confirmed, int Cancelled)> CountForPeriodAsync(Guid workspaceId, DateTime from, DateTime to, DateTime today, DateTime tomorrow, CancellationToken ct);
    void Add(Reservation reservation);
    Task<bool> HasFutureConfirmedAtLocationAsync(Guid workspaceId, string locationId, CancellationToken cancellationToken);

    Task<Reservation?> GetActiveReservationByPhoneAsync(Guid workspaceId, string customerIdentifier, CancellationToken cancellationToken);

    Task<Reservation?> GetByIdAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken);
    Task<IEnumerable<Reservation>> GetReservationsForDateAsync(Guid workspaceId, string locationId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);
    Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, string locationId, DateTime startTimeUtc, DateTime endTimeUtc, Guid? excludeReservationId = null, CancellationToken cancellationToken = default);
}
