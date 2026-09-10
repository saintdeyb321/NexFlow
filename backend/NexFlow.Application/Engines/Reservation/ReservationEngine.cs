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
    private readonly ICatalogRepository _catalogRepository; // 🔥 SPRINT 3: Reemplazo
    private readonly IBusinessHoursRepository _hoursRepository;
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowGateway _workflowGateway;
    private readonly ILogger<ReservationEngine> _logger;

    public ReservationEngine(
        IReservationRepository reservationRepository,
        ICatalogRepository catalogRepository,
        IBusinessHoursRepository hoursRepository,
        IBusinessProfileRepository profileRepository,
        ILocationRepository locationRepository,
        IUnitOfWork unitOfWork,
        IWorkflowGateway workflowGateway,
        ILogger<ReservationEngine> logger)
    {
        _reservationRepository = reservationRepository;
        _catalogRepository = catalogRepository;
        _hoursRepository = hoursRepository;
        _profileRepository = profileRepository;
        _locationRepository = locationRepository;
        _unitOfWork = unitOfWork;
        _workflowGateway = workflowGateway;
        _logger = logger;
    }

    private async Task<TimeZoneInfo> GetWorkspaceTimeZoneAsync(Guid workspaceId, CancellationToken ct)
    {
        var profile = await _profileRepository.GetProfileAsync(workspaceId, ct);
        var tzId = string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone;

        try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
    }

    public async Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(Guid workspaceId, string locationId, string serviceId, DateTime date, CancellationToken cancellationToken)
    {
        var workspaceZone = await GetWorkspaceTimeZoneAsync(workspaceId, cancellationToken);

        // 🔥 SPRINT 3: Validar que exista y que sea de tipo SERVICE
        var items = await _catalogRepository.GetActiveItemsAsync(workspaceId, cancellationToken);
        var targetService = items.FirstOrDefault(s => s.Id == serviceId && s.Type.ToUpperInvariant() == "SERVICE");

        if (targetService == null || !targetService.IsActive || !targetService.RequiresReservation ||
            (targetService.AvailableAtLocations != null && targetService.AvailableAtLocations.Any() && !targetService.AvailableAtLocations.Contains(locationId)))
            return new List<TimeSlotDto>();

        if (targetService.DurationInMinutes == null || targetService.DurationInMinutes < 5)
            return new List<TimeSlotDto>();

        var slotDuration = TimeSpan.FromMinutes(targetService.DurationInMinutes.Value);
        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);

        var localDate = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDate.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed || !TimeSpan.TryParse(todayHours.OpenTime, out var openTime) || !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
            return new List<TimeSlotDto>();

        var existingReservations = await _reservationRepository.GetReservationsForDateAsync(workspaceId, locationId, localDate, cancellationToken);
        var availableSlots = new List<TimeSlotDto>();

        var currentSlotStartLocal = localDate.Date.Add(openTime);
        var endOfDayLocal = localDate.Date.Add(closeTime);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, workspaceZone);

        while (currentSlotStartLocal.Add(slotDuration) <= endOfDayLocal)
        {
            var currentSlotEndLocal = currentSlotStartLocal.Add(slotDuration);
            var utcSlotStart = TimeZoneInfo.ConvertTimeToUtc(currentSlotStartLocal, workspaceZone);
            var utcSlotEnd = TimeZoneInfo.ConvertTimeToUtc(currentSlotEndLocal, workspaceZone);

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
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        if (locations == null || !locations.Any(l => l.Id == locationId))
            return Result<ReservationDto>.Failure(new Error("Location.NotFound", "La sede seleccionada no existe o no es válida."));

        var workspaceZone = await GetWorkspaceTimeZoneAsync(workspaceId, cancellationToken);
        var localDateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);

        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDateTime.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed ||
            !TimeSpan.TryParse(todayHours.OpenTime, out var openTime) ||
            !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
        {
            return Result<ReservationDto>.Failure(new Error("Reservation.Closed", "El negocio se encuentra cerrado en el día y horario seleccionado."));
        }

        var timeOnly = localDateTime.TimeOfDay;
        var startTimeUtc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, workspaceZone);

        // 🔥 SPRINT 3
        var items = await _catalogRepository.GetActiveItemsAsync(workspaceId, cancellationToken);
        var targetService = items.FirstOrDefault(s => s.Id == serviceId && s.Type.ToUpperInvariant() == "SERVICE");

        if (targetService == null || !targetService.IsActive)
            return Result<ReservationDto>.Failure(new Error("Service.NotFound", "El servicio no existe o se encuentra inactivo."));

        if (!targetService.RequiresReservation)
            return Result<ReservationDto>.Failure(new Error("Service.NotReservable", "Este servicio no requiere ni acepta reservas."));

        if (targetService.DurationInMinutes == null || targetService.DurationInMinutes < 5)
            return Result<ReservationDto>.Failure(new Error("Service.InvalidDuration", "La duración del servicio es inválida para operar una reserva."));

        if (targetService.AvailableAtLocations != null && targetService.AvailableAtLocations.Any() && !targetService.AvailableAtLocations.Contains(locationId))
            return Result<ReservationDto>.Failure(new Error("Service.NotAvailable", "Este servicio no se ofrece en la sede seleccionada."));

        var endTimeUtc = startTimeUtc.AddMinutes(targetService.DurationInMinutes.Value);
        var localEndTime = localDateTime.AddMinutes(targetService.DurationInMinutes.Value);

        if (timeOnly < openTime || localEndTime.TimeOfDay > closeTime)
            return Result<ReservationDto>.Failure(new Error("Reservation.OutOfHours", "La hora solicitada está fuera del horario comercial de la sede."));

        using (var scope = new TransactionScope(TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) },
            TransactionScopeAsyncFlowOption.Enabled))
        {
            var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, locationId, startTimeUtc, endTimeUtc, null, cancellationToken);
            if (!isAvailable) return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El horario ya fue tomado por otro cliente."));

            var reservation = Domain.Entities.Reservation.Create(workspaceId, locationId, serviceId, customerIdentifier, customerName, startTimeUtc, endTimeUtc);
            _reservationRepository.Add(reservation);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();

            var dto = new ReservationDto(reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId, reservation.CustomerIdentifier, reservation.CustomerName, reservation.StartTime, reservation.Status.ToString());
            _ = TriggerN8nSafeAsync("RESERVATION_CREATED", workspaceId, dto, reservation.Id, cancellationToken);

            return Result<ReservationDto>.Success(dto);
        }
    }

    public async Task<Result<ReservationDto>> EditReservationAsync(Guid workspaceId, Guid reservationId, DateTime newDateTime, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result<ReservationDto>.Failure(new Error("Reservation.NotFound", "La reserva no existe."));

        var workspaceZone = await GetWorkspaceTimeZoneAsync(workspaceId, cancellationToken);
        var localDateTime = DateTime.SpecifyKind(newDateTime, DateTimeKind.Unspecified);

        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, reservation.LocationId, cancellationToken);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDateTime.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed ||
            !TimeSpan.TryParse(todayHours.OpenTime, out var openTime) ||
            !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
        {
            return Result<ReservationDto>.Failure(new Error("Reservation.Closed", "El negocio se encuentra cerrado en el día y horario seleccionado para la reprogramación."));
        }

        var timeOnly = localDateTime.TimeOfDay;
        var newStartTimeUtc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, workspaceZone);

        // 🔥 SPRINT 3
        var items = await _catalogRepository.GetActiveItemsAsync(workspaceId, cancellationToken);
        var targetService = items.FirstOrDefault(s => s.Id == reservation.ServiceId && s.Type.ToUpperInvariant() == "SERVICE");

        if (targetService == null || !targetService.IsActive)
            return Result<ReservationDto>.Failure(new Error("Service.NotFound", "El servicio original no existe o se encuentra inactivo."));

        if (!targetService.RequiresReservation)
            return Result<ReservationDto>.Failure(new Error("Service.NotReservable", "Este servicio no requiere ni acepta reservas."));

        if (targetService.DurationInMinutes == null || targetService.DurationInMinutes < 5)
            return Result<ReservationDto>.Failure(new Error("Service.InvalidDuration", "La duración del servicio es inválida."));

        if (targetService.AvailableAtLocations != null && targetService.AvailableAtLocations.Any() && !targetService.AvailableAtLocations.Contains(reservation.LocationId))
            return Result<ReservationDto>.Failure(new Error("Service.NotAvailable", "Este servicio ya no se ofrece en la sede actual."));

        var newEndTimeUtc = newStartTimeUtc.AddMinutes(targetService.DurationInMinutes.Value);
        var localEndTime = localDateTime.AddMinutes(targetService.DurationInMinutes.Value);

        if (timeOnly < openTime || localEndTime.TimeOfDay > closeTime)
            return Result<ReservationDto>.Failure(new Error("Reservation.OutOfHours", "El nuevo horario solicitado está fuera del horario comercial."));

        using (var scope = new TransactionScope(TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) },
            TransactionScopeAsyncFlowOption.Enabled))
        {
            var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, reservation.LocationId, newStartTimeUtc, newEndTimeUtc, reservation.Id, cancellationToken);
            if (!isAvailable) return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El nuevo horario ya está ocupado por otro cliente."));

            reservation.Reschedule(newStartTimeUtc, newEndTimeUtc);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();
        }

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

    public async Task<Result<Domain.Entities.Reservation>> CancelActiveReservationAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetActiveReservationByPhoneAsync(workspaceId, customerPhone, cancellationToken);
        if (reservation == null)
            return Result<Domain.Entities.Reservation>.Failure(new Error("Reservation.NotFound", "No tienes ninguna reserva activa para cancelar en este momento."));

        reservation.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _ = TriggerN8nSafeAsync("RESERVATION_CANCELLED", workspaceId, new { ReservationId = reservation.Id, Status = "CANCELLED" }, reservation.Id, cancellationToken);

        return Result<Domain.Entities.Reservation>.Success(reservation);
    }

    public async Task<Result> CompleteReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));

        reservation.Complete();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _ = TriggerN8nSafeAsync("RESERVATION_COMPLETED", workspaceId, new { ReservationId = reservation.Id, Status = "COMPLETED" }, reservation.Id, cancellationToken);

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
            _logger.LogError(ex, "Alerta: Falló n8n para la reserva {ReservationId}.", reservationId);
        }
    }
}