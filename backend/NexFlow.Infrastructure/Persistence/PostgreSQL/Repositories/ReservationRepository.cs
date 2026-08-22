using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        var utcDate = date.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : date.ToUniversalTime();

        var startOfDay = DateTime.SpecifyKind(utcDate.Date, DateTimeKind.Utc);
        var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

        return await _context.Reservations
            .Where(r => r.WorkspaceId == workspaceId
                     && r.LocationId == locationId
                     && r.Status != ReservationStatus.Cancelled
                     && r.StartTime >= startOfDay
                     && r.StartTime <= endOfDay)
            .OrderBy(r => r.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, string locationId, string serviceId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken)
    {
        var utcStartTime = startTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
            : startTime.ToUniversalTime();

        var utcEndTime = endTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(endTime, DateTimeKind.Utc)
            : endTime.ToUniversalTime();

        bool hasOverlap = await _context.Reservations
            .AnyAsync(r => r.WorkspaceId == workspaceId
                        && r.LocationId == locationId
                        && r.ServiceId == serviceId
                        && r.Status != ReservationStatus.Cancelled
                        && r.StartTime < utcEndTime
                        && r.EndTime > utcStartTime,
                      cancellationToken);

        return !hasOverlap;
    }
}