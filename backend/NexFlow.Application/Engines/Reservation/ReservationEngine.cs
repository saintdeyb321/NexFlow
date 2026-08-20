using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Engines.Reservation;

public class ReservationEngine : IReservationEngine
{
    private readonly IReservationRepository _reservationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ReservationEngine(IReservationRepository reservationRepository, IUnitOfWork unitOfWork)
    {
        _reservationRepository = reservationRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(Guid workspaceId, Guid locationId, Guid serviceId, DateTime date, CancellationToken cancellationToken)
    {
        // 1. Obtenemos todas las reservas de ese día para esa sede
        var existingReservations = await _reservationRepository.GetReservationsForDateAsync(workspaceId, locationId, date, cancellationToken);

        var availableSlots = new List<TimeSlotDto>();

        // 2. Aquí en el futuro inyectaremos el IBusinessConfigurationRepository (Firestore)
        // para cruzar los horarios de apertura del negocio con existingReservations.
        // Por ahora, generamos un slot de ejemplo demostrando que el flujo estructural está listo.
        var dummyStart = date.Date.AddHours(10);
        availableSlots.Add(new TimeSlotDto(dummyStart, dummyStart.AddMinutes(30), true));

        return availableSlots;
    }

    public async Task<Result<ReservationDto>> CreateReservationAsync(Guid workspaceId, Guid locationId, Guid serviceId, string customerIdentifier, DateTime dateTime, CancellationToken cancellationToken)
    {
        var startTime = dateTime;
        var endTime = dateTime.AddMinutes(30);

        var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, locationId, serviceId, startTime, endTime, cancellationToken);

        if (!isAvailable)
        {
            return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El horario solicitado ya está ocupado."));
        }

        var reservation = Domain.Entities.Reservation.Create(workspaceId, locationId, serviceId, customerIdentifier, startTime, endTime);
        _reservationRepository.Add(reservation);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = new ReservationDto(
            reservation.Id,
            reservation.WorkspaceId,
            reservation.LocationId,
            reservation.ServiceId,
            reservation.CustomerIdentifier,
            reservation.StartTime,
            reservation.Status.ToString());

        return Result<ReservationDto>.Success(dto);
    }

    public async Task<Result> CancelReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);

        if (reservation == null)
            return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));

        if (reservation.Status == ReservationStatus.Cancelled)
            return Result.Failure(new Error("Reservation.AlreadyCancelled", "La reserva ya estaba cancelada."));

        // Lógica pura de dominio
        reservation.Cancel();

        // Impacto real en PostgreSQL
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}