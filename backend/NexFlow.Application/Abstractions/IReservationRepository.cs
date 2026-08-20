using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions;

public interface IReservationRepository
{
    void Add(Reservation reservation);

    // Para cancelar o reprogramar
    Task<Reservation?> GetByIdAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken);

    // Para calcular los horarios libres del día
    Task<IEnumerable<Reservation>> GetReservationsForDateAsync(Guid workspaceId, Guid locationId, DateTime date, CancellationToken cancellationToken);

    // Disponibilidad blindada por Sede y Servicio
    Task<bool> IsTimeSlotAvailableAsync(Guid workspaceId, Guid locationId, Guid serviceId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken);
}