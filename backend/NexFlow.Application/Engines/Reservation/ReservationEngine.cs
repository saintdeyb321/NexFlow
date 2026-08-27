using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Reservations;
using System.Transactions;

namespace NexFlow.Application.Engines.Reservation;

public class ReservationEngine : IReservationEngine
{
    private readonly IReservationRepository _reservationRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly IBusinessHoursRepository _hoursRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowGateway _workflowGateway;
    private readonly ILogger<ReservationEngine> _logger;

    // 🛡️ CORRECCIÓN (Fallo #6): El estándar absoluto para Perú.
    private readonly TimeZoneInfo _peruZone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

    public ReservationEngine(
        IReservationRepository reservationRepository, IServiceRepository serviceRepository,
        IBusinessHoursRepository hoursRepository, IUnitOfWork unitOfWork,
        IWorkflowGateway workflowGateway, ILogger<ReservationEngine> logger)
    {
        _reservationRepository = reservationRepository; _serviceRepository = serviceRepository;
        _hoursRepository = hoursRepository; _unitOfWork = unitOfWork;
        _workflowGateway = workflowGateway; _logger = logger;
    }

    public async Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(Guid workspaceId, string locationId, string serviceId, DateTime date, CancellationToken cancellationToken)
    {
        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);
        var targetService = services.FirstOrDefault(s => s.Id == serviceId);
        if (targetService == null) return new List<TimeSlotDto>();

        var slotDuration = TimeSpan.FromMinutes(targetService.DurationInMinutes);
        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);

        // Fijamos la fecha consultada al horario de Perú
        var localDate = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDate.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed || !TimeSpan.TryParse(todayHours.OpenTime, out var openTime) || !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
            return new List<TimeSlotDto>();

        var existingReservations = await _reservationRepository.GetReservationsForDateAsync(workspaceId, locationId, localDate, cancellationToken);
        var availableSlots = new List<TimeSlotDto>();

        var currentSlotStartLocal = localDate.Date.Add(openTime);
        var endOfDayLocal = localDate.Date.Add(closeTime);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _peruZone);

        while (currentSlotStartLocal.Add(slotDuration) <= endOfDayLocal)
        {
            var currentSlotEndLocal = currentSlotStartLocal.Add(slotDuration);

            // Convertimos el Slot de Perú a UTC para comparar con la Base de Datos
            var utcSlotStart = TimeZoneInfo.ConvertTimeToUtc(currentSlotStartLocal, _peruZone);
            var utcSlotEnd = TimeZoneInfo.ConvertTimeToUtc(currentSlotEndLocal, _peruZone);

            bool isOccupied = existingReservations.Any(r => r.StartTime < utcSlotEnd && r.EndTime > utcSlotStart);
            bool isPast = currentSlotStartLocal <= localNow;

            if (!isOccupied && !isPast)
            {
                availableSlots.Add(new TimeSlotDto(currentSlotStartLocal, currentSlotEndLocal, true));
            }
            currentSlotStartLocal = currentSlotEndLocal;
        }

        return availableSlots;
    }

    public async Task<Result<ReservationDto>> CreateReservationAsync(Guid workspaceId, string locationId, string serviceId, string customerIdentifier, string customerName, DateTime dateTime, CancellationToken cancellationToken)
    {
        // Forzamos que la fecha que envía el frontend se lea como hora de Perú y la pasamos a UTC real.
        var localDateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        var startTimeUtc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, _peruZone);

        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);
        var targetService = services.FirstOrDefault(s => s.Id == serviceId);

        if (targetService == null) return Result<ReservationDto>.Failure(new Error("Service.NotFound", "El servicio no existe."));

        var endTimeUtc = startTimeUtc.AddMinutes(targetService.DurationInMinutes);

        // 🛡️ CORRECCIÓN (Fallo #8): Transacción Serializable. Si hay concurrencia, PostgreSQL bloquea.
        using var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);

        var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, locationId, startTimeUtc, endTimeUtc, null, cancellationToken);
        if (!isAvailable) return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El horario ya está ocupado."));

        var reservation = Domain.Entities.Reservation.Create(workspaceId, locationId, serviceId, customerIdentifier, customerName, startTimeUtc, endTimeUtc);
        _reservationRepository.Add(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        scope.Complete(); // Confirmamos la transacción atómica

        var dto = new ReservationDto(reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId, reservation.CustomerIdentifier, reservation.CustomerName, reservation.StartTime, reservation.Status.ToString());

        // Trigger n8n (aislado de la transacción principal para no bloquear)
        _ = TriggerN8nSafeAsync("RESERVATION_CREATED", workspaceId, dto, reservation.Id, cancellationToken);

        return Result<ReservationDto>.Success(dto);
    }

    // 🛡️ CORRECCIÓN (Fallo #45): Agregamos el método de Edición (Reagendamiento)
    public async Task<Result<ReservationDto>> EditReservationAsync(Guid workspaceId, Guid reservationId, DateTime newDateTime, CancellationToken cancellationToken)
    {
        var localDateTime = DateTime.SpecifyKind(newDateTime, DateTimeKind.Unspecified);
        var newStartTimeUtc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, _peruZone);

        using var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);

        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result<ReservationDto>.Failure(new Error("Reservation.NotFound", "La reserva no existe."));

        var services = await _serviceRepository.GetServicesAsync(workspaceId, cancellationToken);
        var targetService = services.FirstOrDefault(s => s.Id == reservation.ServiceId);
        if (targetService == null) return Result<ReservationDto>.Failure(new Error("Service.NotFound", "El servicio original no existe."));

        var newEndTimeUtc = newStartTimeUtc.AddMinutes(targetService.DurationInMinutes);

        // Verificamos disponibilidad excluyendo la reserva actual
        var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, reservation.LocationId, newStartTimeUtc, newEndTimeUtc, reservation.Id, cancellationToken);
        if (!isAvailable) return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El nuevo horario ya está ocupado."));

        reservation.Reschedule(newStartTimeUtc, newEndTimeUtc);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        scope.Complete();

        var dto = new ReservationDto(reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId, reservation.CustomerIdentifier, reservation.CustomerName, reservation.StartTime, reservation.Status.ToString());
        _ = TriggerN8nSafeAsync("RESERVATION_RESCHEDULED", workspaceId, dto, reservation.Id, cancellationToken);

        return Result<ReservationDto>.Success(dto);
    }

    public async Task<Result> CancelReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));

        reservation.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _ = TriggerN8nSafeAsync("RESERVATION_CANCELLED", workspaceId, new { ReservationId = reservation.Id, Status = "CANCELLED" }, reservation.Id, cancellationToken);

        return Result.Success();
    }

    private async Task TriggerN8nSafeAsync(string eventType, Guid workspaceId, object data, Guid reservationId, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new N8nEventPayload<object>(workspaceId, eventType, Guid.NewGuid().ToString(), $"{eventType}_{reservationId}", DateTime.UtcNow, data);
            await _workflowGateway.TriggerWorkflowAsync("nexflow-events", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alerta: Falló el envío del evento {EventType} a n8n para la reserva {ReservationId}.", eventType, reservationId);
        }
    }
}