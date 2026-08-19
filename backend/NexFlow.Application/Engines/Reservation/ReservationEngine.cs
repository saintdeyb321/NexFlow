using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Application.DTOs.Reservations;

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
        // TODO: En el siguiente paso implementaremos la lectura de horarios libres. 
        // Por ahora devolvemos vacío para cumplir tu contrato y compilar.
        return new List<TimeSlotDto>();
    }

    public async Task<Result<ReservationDto>> CreateReservationAsync(Guid workspaceId, Guid locationId, Guid serviceId, string customerIdentifier, DateTime dateTime, CancellationToken cancellationToken)
    {
        // Asumimos 30 minutos de duración para la reserva por defecto (luego lo sacaremos del Service)
        var startTime = dateTime;
        var endTime = dateTime.AddMinutes(30);

        var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, startTime, endTime, cancellationToken);

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
            reservation.Status);

        return Result<ReservationDto>.Success(dto);
    }

    public async Task<Result> CancelReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        // TODO: Implementaremos el UPDATE en BD para cancelar
        return Result.Success();
    }
}