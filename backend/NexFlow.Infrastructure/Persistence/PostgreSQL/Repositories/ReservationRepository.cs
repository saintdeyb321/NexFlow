using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class ReservationRepository : IReservationRepository
{
    private readonly NexFlowDbContext _context;

    public ReservationRepository(NexFlowDbContext context) => _context = context;

    public void Add(Domain.Entities.Reservation reservation) => _context.Reservations.Add(reservation);

    public async Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken)
    {
        // La matemática del cruce de fechas (Overlap)
        // Dos periodos se cruzan si: StartA < EndB Y EndA > StartB
        bool hasOverlap = await _context.Reservations
            .AnyAsync(r => r.WorkspaceId == workspaceId
                        && r.Status != "Cancelled"
                        && r.StartTime < endTime
                        && r.EndTime > startTime,
                      cancellationToken);

        // Está disponible si NO hay cruces
        return !hasOverlap;
    }
}