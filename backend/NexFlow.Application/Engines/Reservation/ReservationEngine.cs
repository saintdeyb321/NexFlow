using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IBusinessConfigurationRepository _businessConfigRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ReservationEngine(
        IReservationRepository reservationRepository,
        IBusinessConfigurationRepository businessConfigRepository,
        IUnitOfWork unitOfWork)
    {
        _reservationRepository = reservationRepository;
        _businessConfigRepository = businessConfigRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(Guid workspaceId, Guid locationId, Guid serviceId, DateTime date, CancellationToken cancellationToken)
    {
        // 1. Validar Servicio y obtener su duración REAL
        var services = await _businessConfigRepository.GetServicesAsync(workspaceId, cancellationToken);
        var targetService = services.FirstOrDefault(s => s.Id == serviceId);
        if (targetService == null) return new List<TimeSlotDto>(); // El servicio no existe

        var slotDuration = TimeSpan.FromMinutes(targetService.DurationInMinutes);

        // 2. Obtener horario de apertura de la sede desde Firestore
        var businessHours = await _businessConfigRepository.GetBusinessHoursAsync(workspaceId, locationId.ToString(), cancellationToken);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)date.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed) return new List<TimeSlotDto>();

        if (!TimeSpan.TryParse(todayHours.OpenTime, out var openTime) || !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
            return new List<TimeSlotDto>();

        // 3. Obtener reservas actuales
        var existingReservations = await _reservationRepository.GetReservationsForDateAsync(workspaceId, locationId, date, cancellationToken);
        var availableSlots = new List<TimeSlotDto>();

        var currentSlotStart = date.Date.Add(openTime);
        var endOfDay = date.Date.Add(closeTime);

        // 4. Algoritmo dinámico
        while (currentSlotStart.Add(slotDuration) <= endOfDay)
        {
            var currentSlotEnd = currentSlotStart.Add(slotDuration);

            bool isOccupied = existingReservations.Any(r => r.StartTime < currentSlotEnd && r.EndTime > currentSlotStart);

            if (!isOccupied)
            {
                availableSlots.Add(new TimeSlotDto(currentSlotStart, currentSlotEnd, true));
            }

            currentSlotStart = currentSlotEnd;
        }

        return availableSlots;
    }

    public async Task<Result<ReservationDto>> CreateReservationAsync(Guid workspaceId, Guid locationId, Guid serviceId, string customerIdentifier, DateTime dateTime, CancellationToken cancellationToken)
    {
        // 1. Obtener la duración real para calcular el EndTime
        var services = await _businessConfigRepository.GetServicesAsync(workspaceId, cancellationToken);
        var targetService = services.FirstOrDefault(s => s.Id == serviceId);

        if (targetService == null)
            return Result<ReservationDto>.Failure(new Error("Service.NotFound", "El servicio solicitado no existe."));

        var startTime = dateTime;
        var endTime = dateTime.AddMinutes(targetService.DurationInMinutes); // Adiós al "30" hardcodeado

        // 2. Validación de disponibilidad lógica
        var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, locationId, serviceId, startTime, endTime, cancellationToken);

        if (!isAvailable)
            return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El horario solicitado ya está ocupado."));

        // 3. Persistencia
        var reservation = Domain.Entities.Reservation.Create(workspaceId, locationId, serviceId, customerIdentifier, startTime, endTime);
        _reservationRepository.Add(reservation);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = new ReservationDto(reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId, reservation.CustomerIdentifier, reservation.StartTime, reservation.Status.ToString());
        return Result<ReservationDto>.Success(dto);
    }

    public async Task<Result> CancelReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));
        if (reservation.Status == ReservationStatus.Cancelled) return Result.Failure(new Error("Reservation.AlreadyCancelled", "La reserva ya estaba cancelada."));

        reservation.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}