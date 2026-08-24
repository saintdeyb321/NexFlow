using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Engines.Reservation;

public class ReservationEngine : IReservationEngine
{
    private readonly IReservationRepository _reservationRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly IBusinessHoursRepository _hoursRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowGateway _workflowGateway;
    private readonly ILogger<ReservationEngine> _logger;

    public ReservationEngine(
        IReservationRepository reservationRepository,
        IServiceRepository serviceRepository,
        IBusinessHoursRepository hoursRepository,
        IUnitOfWork unitOfWork,
        IWorkflowGateway workflowGateway,
        ILogger<ReservationEngine> logger)
    {
        _reservationRepository = reservationRepository;
        _serviceRepository = serviceRepository;
        _hoursRepository = hoursRepository;
        _unitOfWork = unitOfWork;
        _workflowGateway = workflowGateway;
        _logger = logger;
    }

    public async Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(Guid workspaceId, string locationId, string serviceId, DateTime date, CancellationToken cancellationToken)
    {
        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);
        var targetService = services.FirstOrDefault(s => s.Id == serviceId);
        if (targetService == null) return new List<TimeSlotDto>();

        var slotDuration = TimeSpan.FromMinutes(targetService.DurationInMinutes);
        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);

        // 🔥 CORRECCIÓN SPRINT 6: Ajuste al Huso Horario UTC-5 para evitar bloqueos nocturnos
        var regionalOffset = TimeSpan.FromHours(-5);
        var localDate = date.Kind == DateTimeKind.Utc ? date.Add(regionalOffset) : date;

        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDate.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed) return new List<TimeSlotDto>();

        if (!TimeSpan.TryParse(todayHours.OpenTime, out var openTime) || !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
            return new List<TimeSlotDto>();

        var existingReservations = await _reservationRepository.GetReservationsForDateAsync(workspaceId, locationId, date, cancellationToken);
        var availableSlots = new List<TimeSlotDto>();

        var currentSlotStart = localDate.Date.Add(openTime);
        var endOfDay = localDate.Date.Add(closeTime);
        var localNow = DateTime.UtcNow.Add(regionalOffset);

        while (currentSlotStart.Add(slotDuration) <= endOfDay)
        {
            var currentSlotEnd = currentSlotStart.Add(slotDuration);

            // Evaluar ocupación con el offset aplicado y descartar horarios que ya pasaron hoy
            bool isOccupied = existingReservations.Any(r => r.StartTime.Add(regionalOffset) < currentSlotEnd && r.EndTime.Add(regionalOffset) > currentSlotStart);
            bool isPast = localDate.Date == localNow.Date && currentSlotStart <= localNow;

            if (!isOccupied && !isPast)
            {
                availableSlots.Add(new TimeSlotDto(currentSlotStart, currentSlotEnd, true));
            }
            currentSlotStart = currentSlotEnd;
        }

        return availableSlots;
    }

    public async Task<Result<ReservationDto>> CreateReservationAsync(Guid workspaceId, string locationId, string serviceId, string customerIdentifier, string customerName, DateTime dateTime, CancellationToken cancellationToken)
    {
        var safeUtcDateTime = dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime.ToUniversalTime();

        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);
        var targetService = services.FirstOrDefault(s => s.Id == serviceId);

        if (targetService == null)
            return Result<ReservationDto>.Failure(new Error("Service.NotFound", "El servicio solicitado no existe."));

        var startTime = safeUtcDateTime;
        var endTime = safeUtcDateTime.AddMinutes(targetService.DurationInMinutes);

        var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, locationId, serviceId, startTime, endTime, cancellationToken);

        if (!isAvailable)
            return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El horario solicitado ya está ocupado."));

        var reservation = Domain.Entities.Reservation.Create(workspaceId, locationId, serviceId, customerIdentifier, customerName, startTime, endTime);
        _reservationRepository.Add(reservation);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = new ReservationDto(reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId, reservation.CustomerIdentifier, reservation.CustomerName, reservation.StartTime, reservation.Status.ToString());

        try
        {
            var payload = new N8nEventPayload<ReservationDto>(
                WorkspaceId: workspaceId,
                EventType: "RESERVATION_CREATED",
                CorrelationId: Guid.NewGuid().ToString(),
                IdempotencyKey: $"CREATE_{reservation.Id}",
                Timestamp: DateTime.UtcNow,
                Data: dto
            );
            await _workflowGateway.TriggerWorkflowAsync("nexflow-events", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alerta: La reserva {ReservationId} se guardó, pero falló el envío del evento a n8n.", reservation.Id);
        }

        return Result<ReservationDto>.Success(dto);
    }

    public async Task<Result> CancelReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));
        if (reservation.Status == ReservationStatus.Cancelled) return Result.Failure(new Error("Reservation.AlreadyCancelled", "La reserva ya estaba cancelada."));

        reservation.Cancel();

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var payload = new N8nEventPayload<object>(
                WorkspaceId: workspaceId,
                EventType: "RESERVATION_CANCELLED",
                CorrelationId: Guid.NewGuid().ToString(),
                IdempotencyKey: $"CANCEL_{reservation.Id}",
                Timestamp: DateTime.UtcNow,
                Data: new { ReservationId = reservation.Id, Status = "CANCELLED" }
            );

            await _workflowGateway.TriggerWorkflowAsync("nexflow-events", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alerta: La reserva {ReservationId} se canceló, pero falló el envío del evento RESERVATION_CANCELLED a n8n.", reservation.Id);
        }

        return Result.Success();
    }
}