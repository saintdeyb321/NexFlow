using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Business.LocationAvailability;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities.System;
// 🔥 NUEVOS NAMESPACES
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Services.DTOs;
using System.Transactions;

namespace NexFlow.Application.Engines.Reservation;

public class ReservationEngine : IReservationEngine
{
    private readonly IReservationRepository _reservationRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IBusinessHoursRepository _hoursRepository;
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowGateway _workflowGateway;
    private readonly ILogger<ReservationEngine> _logger;
    private readonly ILocationAvailabilityService _locationAvailabilityService;
    private readonly IClock _clock;
    private readonly IOutboxRepository _outboxRepository;

    public ReservationEngine(
        IReservationRepository reservationRepository, ICatalogRepository catalogRepository,
        IBusinessHoursRepository hoursRepository, IBusinessProfileRepository profileRepository,
        ILocationRepository locationRepository, IUnitOfWork unitOfWork,
        IWorkflowGateway workflowGateway, ILogger<ReservationEngine> logger,
        ILocationAvailabilityService locationAvailabilityService,
        IClock clock, IOutboxRepository outboxRepository)
    {
        _reservationRepository = reservationRepository; _catalogRepository = catalogRepository;
        _hoursRepository = hoursRepository; _profileRepository = profileRepository;
        _locationRepository = locationRepository; _unitOfWork = unitOfWork;
        _workflowGateway = workflowGateway; _logger = logger;
        _locationAvailabilityService = locationAvailabilityService;
        _clock = clock;
        _outboxRepository = outboxRepository;
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
        var items = await _catalogRepository.GetActiveItemsAsync(workspaceId, cancellationToken);

        var targetService = items.FirstOrDefault(s => s.Id == serviceId && string.Equals(s.Type, "SERVICE", StringComparison.OrdinalIgnoreCase)) as ServiceDto;

        if (targetService == null || !targetService.IsActive || !targetService.RequiresReservation || !_locationAvailabilityService.IsOfferingAvailableAtLocation(targetService, locationId))
            return new List<TimeSlotDto>();

        if (!targetService.DurationInMinutes.HasValue || targetService.DurationInMinutes.Value < 5)
            return new List<TimeSlotDto>();

        var slotDuration = TimeSpan.FromMinutes(targetService.DurationInMinutes.Value);
        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);

        var localDate = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDate.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed || !TimeSpan.TryParse(todayHours.OpenTime, out var openTime) || !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
            return new List<TimeSlotDto>();

        var startOfDayUtc = TimeZoneInfo.ConvertTimeToUtc(localDate, workspaceZone);
        var endOfDayUtc = startOfDayUtc.AddDays(1);

        var existingReservations = await _reservationRepository.GetReservationsForDateAsync(workspaceId, locationId, startOfDayUtc, endOfDayUtc, cancellationToken);
        var availableSlots = new List<TimeSlotDto>();

        var currentSlotStartLocal = localDate.Date.Add(openTime);
        var endOfDayLocal = localDate.Date.Add(closeTime);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, workspaceZone);

        while (currentSlotStartLocal.Add(slotDuration) <= endOfDayLocal)
        {
            var currentSlotEndLocal = currentSlotStartLocal.Add(slotDuration);
            var utcSlotStart = TimeZoneInfo.ConvertTimeToUtc(currentSlotStartLocal, workspaceZone);
            var utcSlotEnd = TimeZoneInfo.ConvertTimeToUtc(currentSlotEndLocal, workspaceZone);

            bool isOccupied = existingReservations.Any(r => r.StartTime < utcSlotEnd && r.EndTime > utcSlotStart);
            bool isPast = currentSlotStartLocal <= localNow;

            if (!isOccupied && !isPast) availableSlots.Add(new TimeSlotDto(currentSlotStartLocal, currentSlotEndLocal, true));
            currentSlotStartLocal = currentSlotEndLocal;
        }

        return availableSlots;
    }

    public async Task<Result<ReservationDto>> CreateReservationAsync(Guid workspaceId, string locationId, string serviceId, string customerIdentifier, string customerName, DateTime dateTime, CancellationToken cancellationToken)
    {
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        if (locations == null || !locations.Any(l => l.Id == locationId))
            return Result<ReservationDto>.Failure(new Error("Location.NotFound", "La sede seleccionada no existe."));

        var workspaceZone = await GetWorkspaceTimeZoneAsync(workspaceId, cancellationToken);
        var localDateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);

        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDateTime.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed || !TimeSpan.TryParse(todayHours.OpenTime, out var openTime) || !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
            return Result<ReservationDto>.Failure(new Error("Reservation.Closed", "El negocio se encuentra cerrado en el día y horario seleccionado."));

        var timeOnly = localDateTime.TimeOfDay;
        var startTimeUtc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, workspaceZone);

        var items = await _catalogRepository.GetActiveItemsAsync(workspaceId, cancellationToken);
        var targetService = items.FirstOrDefault(s => s.Id == serviceId && string.Equals(s.Type, "SERVICE", StringComparison.OrdinalIgnoreCase)) as ServiceDto;

        if (targetService == null || !targetService.IsActive) return Result<ReservationDto>.Failure(new Error("Service.NotFound", "El servicio no existe o está inactivo."));
        if (!targetService.RequiresReservation) return Result<ReservationDto>.Failure(new Error("Service.NotReservable", "Este servicio no requiere reservas."));
        if (!targetService.DurationInMinutes.HasValue || targetService.DurationInMinutes.Value < 5) return Result<ReservationDto>.Failure(new Error("Service.InvalidDuration", "Duración inválida."));

        if (!_locationAvailabilityService.IsOfferingAvailableAtLocation(targetService, locationId))
            return Result<ReservationDto>.Failure(new Error("Service.NotAvailable", "Servicio no disponible en esta sede."));

        var endTimeUtc = startTimeUtc.AddMinutes(targetService.DurationInMinutes.Value);
        var localEndTime = localDateTime.AddMinutes(targetService.DurationInMinutes.Value);

        if (timeOnly < openTime || localEndTime.TimeOfDay > closeTime)
            return Result<ReservationDto>.Failure(new Error("Reservation.OutOfHours", "La hora solicitada está fuera del horario comercial."));

        using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
        {
            var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, locationId, startTimeUtc, endTimeUtc, null, cancellationToken);
            if (!isAvailable) return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El horario ya fue tomado por otro cliente."));

            var reservation = Domain.Entities.Reservation.Create(workspaceId, locationId, serviceId, customerIdentifier, customerName, startTimeUtc, endTimeUtc);
            _reservationRepository.Add(reservation);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();

            var dto = new ReservationDto(reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId, reservation.CustomerIdentifier, reservation.CustomerName, reservation.StartTime, reservation.Status.ToString());

            var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_CREATED", Guid.NewGuid().ToString(), $"res_{reservation.Id}", DateTime.UtcNow, dto);
            var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_CREATED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
            await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

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

        if (todayHours == null || todayHours.IsClosed || !TimeSpan.TryParse(todayHours.OpenTime, out var openTime) || !TimeSpan.TryParse(todayHours.CloseTime, out var closeTime))
            return Result<ReservationDto>.Failure(new Error("Reservation.Closed", "El negocio se encuentra cerrado en el día y horario seleccionado."));

        var timeOnly = localDateTime.TimeOfDay;
        var newStartTimeUtc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, workspaceZone);

        var items = await _catalogRepository.GetActiveItemsAsync(workspaceId, cancellationToken);
        var targetService = items.FirstOrDefault(s => s.Id == reservation.ServiceId && string.Equals(s.Type, "SERVICE", StringComparison.OrdinalIgnoreCase)) as ServiceDto;

        if (targetService == null || !targetService.IsActive) return Result<ReservationDto>.Failure(new Error("Service.NotFound", "Servicio no válido."));
        if (!targetService.RequiresReservation) return Result<ReservationDto>.Failure(new Error("Service.NotReservable", "Servicio no reservable."));
        if (!targetService.DurationInMinutes.HasValue || targetService.DurationInMinutes.Value < 5) return Result<ReservationDto>.Failure(new Error("Service.InvalidDuration", "Duración inválida."));

        if (!_locationAvailabilityService.IsOfferingAvailableAtLocation(targetService, reservation.LocationId))
            return Result<ReservationDto>.Failure(new Error("Service.NotAvailable", "Servicio no disponible en sede."));

        var newEndTimeUtc = newStartTimeUtc.AddMinutes(targetService.DurationInMinutes.Value);
        var localEndTime = localDateTime.AddMinutes(targetService.DurationInMinutes.Value);

        if (timeOnly < openTime || localEndTime.TimeOfDay > closeTime)
            return Result<ReservationDto>.Failure(new Error("Reservation.OutOfHours", "Fuera del horario comercial."));

        using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
        {
            var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, reservation.LocationId, newStartTimeUtc, newEndTimeUtc, reservation.Id, cancellationToken);
            if (!isAvailable) return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El nuevo horario ya está ocupado."));

            reservation.Reschedule(newStartTimeUtc, newEndTimeUtc);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();
        }

        var dto = new ReservationDto(reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId, reservation.CustomerIdentifier, reservation.CustomerName, reservation.StartTime, reservation.Status.ToString());

        var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_RESCHEDULED", Guid.NewGuid().ToString(), $"res_upd_{reservation.Id}", DateTime.UtcNow, dto);
        var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_RESCHEDULED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
        await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

        return Result<ReservationDto>.Success(dto);
    }

    public async Task<Result> CancelReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));

        reservation.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_CANCELLED", Guid.NewGuid().ToString(), $"res_can_{reservation.Id}", DateTime.UtcNow, new { ReservationId = reservation.Id, Status = "CANCELLED" });
        var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_CANCELLED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
        await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<Domain.Entities.Reservation>> CancelActiveReservationAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetActiveReservationByPhoneAsync(workspaceId, customerPhone, cancellationToken);
        if (reservation == null) return Result<Domain.Entities.Reservation>.Failure(new Error("Reservation.NotFound", "No tienes ninguna reserva activa."));

        reservation.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_CANCELLED", Guid.NewGuid().ToString(), $"res_can_{reservation.Id}", DateTime.UtcNow, new { ReservationId = reservation.Id, Status = "CANCELLED" });
        var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_CANCELLED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
        await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

        return Result<Domain.Entities.Reservation>.Success(reservation);
    }

    public async Task<Result> CompleteReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));

        reservation.Complete();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_COMPLETED", Guid.NewGuid().ToString(), $"res_comp_{reservation.Id}", DateTime.UtcNow, new { ReservationId = reservation.Id, Status = "COMPLETED" });
        var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_COMPLETED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
        await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

        return Result.Success();
    }
}