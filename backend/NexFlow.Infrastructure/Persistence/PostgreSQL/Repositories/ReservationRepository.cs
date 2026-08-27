using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class ReservationRepository : IReservationRepository
{
    private readonly NexFlowDbContext _context;

    public ReservationRepository(NexFlowDbContext context) => _context = context;

    // CORRECCIÓN: Volvemos a la normalidad, respetando la encapsulación de DDD.
    public void Add(Reservation reservation) => _context.Reservations.Add(reservation);

    public async Task<Reservation?> GetByIdAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        return await _context.Reservations
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.WorkspaceId == workspaceId, cancellationToken);
    }

    public async Task<IEnumerable<Reservation>> GetReservationsForDateAsync(Guid workspaceId, string locationId, DateTime date, CancellationToken cancellationToken)
    {
        // 🔥 CORRECCIÓN (Fallo #9): Calculamos el día exacto en Perú y lo pasamos a UTC
        var peruZone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        var peruDate = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(peruDate, peruZone);
        var endUtc = startUtc.AddDays(1);

        return await _context.Reservations
            .Where(r => r.WorkspaceId == workspaceId
                     && r.LocationId == locationId
                     && r.Status != ReservationStatus.Cancelled
                     && r.StartTime >= startUtc
                     && r.StartTime < endUtc)
            .OrderBy(r => r.StartTime)
            .ToListAsync(cancellationToken);
    }

    // 🔥 CORRECCIÓN (Fallo #7 y #45): Quitamos serviceId (el recurso físico es la sede) y añadimos excludeReservationId
    public async Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, string locationId, DateTime startTime, DateTime endTime, Guid? excludeReservationId = null, CancellationToken cancellationToken = default)
    {
        var utcStartTime = startTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
            : startTime.ToUniversalTime();

        var utcEndTime = endTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(endTime, DateTimeKind.Utc)
            : endTime.ToUniversalTime();

        var query = _context.Reservations
            .Where(r => r.WorkspaceId == workspaceId
                     && r.LocationId == locationId
                     // ELIMINADO: && r.ServiceId == serviceId (para bloquear la sede completa)
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