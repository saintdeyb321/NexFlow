using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class ReservationRepository : IReservationRepository
{
    private readonly NexFlowDbContext _context;
    private readonly IClock _clock;

    public ReservationRepository(NexFlowDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<(int Today, int Confirmed, int Cancelled)> CountForPeriodAsync(Guid workspaceId, DateTime from, DateTime to, DateTime today, DateTime tomorrow, CancellationToken ct)
    {
        var counts = await _context.Reservations.Where(r => r.WorkspaceId == workspaceId && r.StartTime >= from && r.StartTime < to)
            .GroupBy(r => r.WorkspaceId).Select(g => new {
                Today = g.Count(r => r.StartTime >= today && r.StartTime < tomorrow),
                Confirmed = g.Count(r => r.Status == ReservationStatus.Confirmed),
                Cancelled = g.Count(r => r.Status == ReservationStatus.Cancelled)
            }).SingleOrDefaultAsync(ct);
        return counts == null ? (0, 0, 0) : (counts.Today, counts.Confirmed, counts.Cancelled);
    }

    public void Add(Reservation reservation) => _context.Reservations.Add(reservation);

    public Task<Reservation?> GetBySourceMessageIdAsync(Guid workspaceId, string sourceMessageId, CancellationToken cancellationToken) =>
        _context.Reservations.AsNoTracking().SingleOrDefaultAsync(
            r => r.WorkspaceId == workspaceId && r.SourceMessageId == sourceMessageId, cancellationToken);

    public Task<bool> HasFutureConfirmedAtLocationAsync(Guid workspaceId, string locationId, CancellationToken cancellationToken) =>
        _context.Reservations.AnyAsync(r => r.WorkspaceId == workspaceId && r.LocationId == locationId
            && r.Status == ReservationStatus.Confirmed && r.StartTime >= _clock.UtcNow, cancellationToken);

    public async Task<Reservation?> GetByIdAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        return await _context.Reservations
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.WorkspaceId == workspaceId, cancellationToken);
    }

    public async Task<Reservation?> GetActiveReservationByPhoneAsync(Guid workspaceId, string customerIdentifier, CancellationToken cancellationToken)
    {

        var nowUtc = _clock.UtcNow;

        return await _context.Reservations
            .Where(r => r.WorkspaceId == workspaceId
                     && r.CustomerIdentifier == customerIdentifier
                     && r.Status == ReservationStatus.Confirmed
                     && r.StartTime >= nowUtc)
            .OrderBy(r => r.StartTime)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Reservation>> GetReservationsForDateAsync(Guid workspaceId, string? locationId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {

        var query = _context.Reservations
            .Where(r => r.WorkspaceId == workspaceId
                     && r.StartTime >= startUtc
                     && r.StartTime < endUtc);

        if (!string.IsNullOrEmpty(locationId))
            query = query.Where(r => r.LocationId == locationId);

        return await query
            .OrderBy(r => r.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, string? locationId, DateTime startTime, DateTime endTime, Guid? excludeReservationId = null, CancellationToken cancellationToken = default)
    {
        var utcStartTime = startTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(startTime, DateTimeKind.Utc) : startTime.ToUniversalTime();
        var utcEndTime = endTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(endTime, DateTimeKind.Utc) : endTime.ToUniversalTime();

        var query = _context.Reservations
            .Where(r => r.WorkspaceId == workspaceId
                     && r.Status != ReservationStatus.Cancelled
                     && r.StartTime < utcEndTime
                     && r.EndTime > utcStartTime);

        if (!string.IsNullOrEmpty(locationId))
            query = query.Where(r => r.LocationId == locationId);

        if (excludeReservationId.HasValue)
        {
            query = query.Where(r => r.Id != excludeReservationId.Value);
        }

        bool hasOverlap = await query.AnyAsync(cancellationToken);
        return !hasOverlap;
    }
}
