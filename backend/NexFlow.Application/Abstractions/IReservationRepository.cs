using NexFlow.Domain.Entities;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Abstractions;

public interface IReservationRepository
{
    Task<(int Today, int Confirmed, int Cancelled)> CountForPeriodAsync(Guid workspaceId, DateTime from, DateTime to, DateTime today, DateTime tomorrow, CancellationToken ct);
    void Add(Reservation reservation);
    Task<Reservation?> GetBySourceMessageIdAsync(Guid workspaceId, string sourceMessageId, CancellationToken cancellationToken);
    Task<bool> HasFutureConfirmedAtLocationAsync(Guid workspaceId, string locationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetFutureConfirmedServiceLocationsAsync(Guid workspaceId, string serviceId, CancellationToken cancellationToken);

    Task<Reservation?> GetActiveReservationByPhoneAsync(Guid workspaceId, string customerIdentifier, CancellationToken cancellationToken);

    Task<Reservation?> GetByIdAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken);
    Task<ReservationPage> GetFutureForContactAsync(Guid workspaceId, string canonicalContact, ReservationCursor? after, int pageSize, CancellationToken ct);
    Task<ReservationCandidate?> GetOwnedFutureAsync(Guid workspaceId, string canonicalContact, Guid reservationId, CancellationToken ct);
    // Ambiguity signal only. A unique name is never proof of owner authorization.
    Task<bool> HasUnambiguousFutureOwnerNameAsync(Guid workspaceId, string canonicalContact, CancellationToken ct);
    // Starts in [startUtc,endUtc); null locationId aggregates the authenticated workspace's locations.
    Task<IEnumerable<Reservation>> GetReservationsForDateAsync(Guid workspaceId, string? locationId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);
    Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, string locationId, DateTime startTimeUtc, DateTime endTimeUtc, Guid? excludeReservationId = null, CancellationToken cancellationToken = default);
}
