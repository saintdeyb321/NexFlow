using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions;

public interface IReservationRepository
{
    Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken);
    void Add(Reservation reservation);
}