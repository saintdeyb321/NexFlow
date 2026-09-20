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

    public void Add(Reservation reservation) => _context.Reservations.Add(reservation);

    public async Task<Reservation?> GetByIdAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        return await _context.Reservations
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.WorkspaceId == workspaceId, cancellationToken);
    }

    public async Task<Reservation?> GetActiveReservationByPhoneAsync(Guid workspaceId, string customerIdentifier, CancellationToken cancellationToken)
    {
        // 🔥 SPRINT 6: Usamos IClock inyectado en lugar de DateTime.UtcNow
        var nowUtc = _clock.UtcNow;

        return await _context.Reservations
            .Where(r => r.WorkspaceId == workspaceId
                     && r.CustomerIdentifier == customerIdentifier
                     && r.Status == ReservationStatus.Confirmed
                     && r.StartTime >= nowUtc)
            .OrderBy(r => r.StartTime)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Reservation>> GetReservationsForDateAsync(Guid workspaceId, string locationId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        // 🔥 SPRINT 6: El repositorio ya no adivina el TimeZone. Compara directamente en UTC.
        return await _context.Reservations
            .Where(r => r.WorkspaceId == workspaceId
                     && r.LocationId == locationId
                     && r.Status != ReservationStatus.Cancelled
                     && r.StartTime >= startUtc
                     && r.StartTime < endUtc)
            .OrderBy(r => r.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, string locationId, DateTime startTime, DateTime endTime, Guid? excludeReservationId = null, CancellationToken cancellationToken = default)
    {
        var utcStartTime = startTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(startTime, DateTimeKind.Utc) : startTime.ToUniversalTime();
        var utcEndTime = endTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(endTime, DateTimeKind.Utc) : endTime.ToUniversalTime();

        var query = _context.Reservations
            .Where(r => r.WorkspaceId == workspaceId
                     && r.LocationId == locationId
                     && r.Status != ReservationStatus.Cancelled
                     && r.StartTime < utcEndTime
                     && r.EndTime > utcStartTime);

        if (excludeReservationId.HasValue)
        {
            query = query.Where(r => r.Id != excludeReservationId.Value);
        }

        bool hasOverlap = await query.AnyAsync(cancellationToken);
        return !hasOverlap;
    }
}