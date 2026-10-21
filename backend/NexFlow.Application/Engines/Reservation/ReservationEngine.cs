using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Business.LocationAvailability;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities.System;
using NexFlow.Application.Features.Services.DTOs;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Globalization;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Engines.Reservation;

public class ReservationEngine : IReservationEngine
{
    private readonly IReservationRepository _reservationRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IBusinessHoursRepository _hoursRepository;
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILocationAvailabilityService _locationAvailabilityService;
    private readonly IClock _clock;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IReservationResourceCoordinator _resources;

    public ReservationEngine(
        IReservationRepository reservationRepository,
        ICatalogRepository catalogRepository,
        IBusinessHoursRepository hoursRepository,
        IBusinessProfileRepository profileRepository,
        ILocationRepository locationRepository,
        IUnitOfWork unitOfWork,
        ILocationAvailabilityService locationAvailabilityService,
        IClock clock,
        IOutboxRepository outboxRepository,
        IReservationResourceCoordinator resources)
    {
        _reservationRepository = reservationRepository;
        _catalogRepository = catalogRepository;
        _hoursRepository = hoursRepository;
        _profileRepository = profileRepository;
        _locationRepository = locationRepository;
        _unitOfWork = unitOfWork;
        _locationAvailabilityService = locationAvailabilityService;
        _clock = clock;
        _outboxRepository = outboxRepository;
        _resources = resources;
    }

    private async Task<TimeZoneInfo> GetWorkspaceTimeZoneAsync(Guid workspaceId, CancellationToken ct)
    {
        var profile = await _profileRepository.GetProfileAsync(workspaceId, ct);
        var tzId = string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone;

        try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
    }

    private static bool TryResolveReservationTime(DateTime value, TimeZoneInfo workspaceZone,
        out DateTime localDateTime, out DateTime utcDateTime)
    {
        localDateTime = default;
        utcDateTime = default;

        if (value.Kind == DateTimeKind.Utc)
        {
            utcDateTime = value;
            localDateTime = TimeZoneInfo.ConvertTimeFromUtc(value, workspaceZone);
            return true;
        }

        if (value.Kind != DateTimeKind.Unspecified
            || workspaceZone.IsInvalidTime(value) || workspaceZone.IsAmbiguousTime(value))
            return false;

        localDateTime = value;
        utcDateTime = TimeZoneInfo.ConvertTimeToUtc(value, workspaceZone);
        return true;
    }

    public Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(Guid workspaceId, string locationId, string serviceId, DateTime date, CancellationToken cancellationToken)
        => GetAvailabilityCoreAsync(workspaceId, locationId, serviceId, date, null, cancellationToken);

    public async Task<IEnumerable<TimeSlotDto>> GetAvailabilityForExistingAsync(Guid workspaceId, string canonicalContact, Guid reservationId, DateTime date, CancellationToken ct)
    {
        var owned = await _reservationRepository.GetOwnedFutureAsync(workspaceId, canonicalContact, reservationId, ct);
        if (owned == null || !await _reservationRepository.HasSingleFutureOwnerAsync(workspaceId, canonicalContact, ct)) return [];
        return await GetAvailabilityCoreAsync(workspaceId, owned.LocationId, owned.ServiceId, date, owned.Id, ct);
    }

    private async Task<IEnumerable<TimeSlotDto>> GetAvailabilityCoreAsync(Guid workspaceId, string locationId, string serviceId, DateTime date, Guid? excludeReservationId, CancellationToken cancellationToken)
    {
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        if (!locations.Any(l => l.Id == locationId)) return [];
        var workspaceZone = await GetWorkspaceTimeZoneAsync(workspaceId, cancellationToken);

        // 🔥 SPRINT 07: Optimizamos la consulta para no traer TODO el catálogo.
        var targetService = await _catalogRepository.GetItemByIdAsync(workspaceId, serviceId, cancellationToken) as ServiceDto;

        if (targetService == null || !targetService.IsActive || !targetService.RequiresReservation || !_locationAvailabilityService.IsOfferingAvailableAtLocation(targetService, locationId))
            return new List<TimeSlotDto>();

        if (!targetService.DurationInMinutes.HasValue || targetService.DurationInMinutes.Value < 5)
            return new List<TimeSlotDto>();

        var slotDuration = TimeSpan.FromMinutes(targetService.DurationInMinutes.Value);
        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);

        var localDate = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDate.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed || !TimeSpan.TryParseExact(todayHours.OpenTime, @"hh\:mm", CultureInfo.InvariantCulture, out var openTime) || !TimeSpan.TryParseExact(todayHours.CloseTime, @"hh\:mm", CultureInfo.InvariantCulture, out var closeTime))
            return new List<TimeSlotDto>();

        var startOfDayUtc = TimeZoneInfo.ConvertTimeToUtc(localDate, workspaceZone);
        var endOfDayUtc = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1), workspaceZone);

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

            bool isOccupied = existingReservations.Any(r => r.Id != excludeReservationId && r.Status != Domain.Enums.ReservationStatus.Cancelled && r.StartTime < utcSlotEnd && r.EndTime > utcSlotStart);
            bool isPast = currentSlotStartLocal <= localNow;

            if (!isOccupied && !isPast) availableSlots.Add(new TimeSlotDto(utcSlotStart, utcSlotEnd, true));
            currentSlotStartLocal = currentSlotEndLocal;
        }

        return availableSlots;
    }

    public async Task<Result<ReservationDto>> CreateReservationAsync(Guid workspaceId, string locationId, string serviceId, string customerIdentifier, string customerName, DateTime dateTime, CancellationToken cancellationToken, string? sourceMessageId = null)
    {
        if (sourceMessageId != null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceMessageId);
            var existing = await _reservationRepository.GetBySourceMessageIdAsync(workspaceId, sourceMessageId, cancellationToken);
            if (existing != null) return Result<ReservationDto>.Success(ToDto(existing));
        }
        var resourceVersions = await CaptureResourcesAsync(workspaceId, locationId, serviceId, cancellationToken);
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        if (locations == null || !locations.Any(l => l.Id == locationId))
            return Result<ReservationDto>.Failure(new Error("Location.NotFound", "La sede seleccionada no existe."));

        var workspaceZone = await GetWorkspaceTimeZoneAsync(workspaceId, cancellationToken);
        if (!TryResolveReservationTime(dateTime, workspaceZone, out var localDateTime, out var startTimeUtc))
            return Result<ReservationDto>.Failure(new Error("Reservation.InvalidTime", "Envía una hora local válida y no ambigua."));

        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDateTime.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed || !TimeSpan.TryParseExact(todayHours.OpenTime, @"hh\:mm", CultureInfo.InvariantCulture, out var openTime) || !TimeSpan.TryParseExact(todayHours.CloseTime, @"hh\:mm", CultureInfo.InvariantCulture, out var closeTime))
            return Result<ReservationDto>.Failure(new Error("Reservation.Closed", "El negocio se encuentra cerrado en el día y horario seleccionado."));

        var timeOnly = localDateTime.TimeOfDay;

        // 🔥 SPRINT 07: Optimizamos. Traemos solo 1 servicio, no el catálogo entero.
        var targetService = await _catalogRepository.GetItemByIdAsync(workspaceId, serviceId, cancellationToken) as ServiceDto;

        if (targetService == null) return Result<ReservationDto>.Failure(new Error("Service.NotFound", "El servicio no existe."));
        if (!targetService.IsActive) return Result<ReservationDto>.Failure(new Error("Service.Inactive", "El servicio está inactivo."));
        if (!targetService.RequiresReservation) return Result<ReservationDto>.Failure(new Error("Service.NotReservable", "Este servicio no requiere reservas."));
        if (!targetService.DurationInMinutes.HasValue || targetService.DurationInMinutes.Value < 5) return Result<ReservationDto>.Failure(new Error("Service.InvalidDuration", "Duración inválida."));

        if (!_locationAvailabilityService.IsOfferingAvailableAtLocation(targetService, locationId))
            return Result<ReservationDto>.Failure(new Error("Service.NotAvailable", "Servicio no disponible en esta sede."));

        var endTimeUtc = startTimeUtc.AddMinutes(targetService.DurationInMinutes.Value);
        var localEndTime = TimeZoneInfo.ConvertTimeFromUtc(endTimeUtc, workspaceZone);

        if (timeOnly < openTime || localEndTime.Date != localDateTime.Date || localEndTime.TimeOfDay > closeTime)
            return Result<ReservationDto>.Failure(new Error("Reservation.OutOfHours", "La hora solicitada está fuera del horario comercial."));

        Domain.Entities.Reservation? created = null;
        OutboxMessage? createdEvent = null;
        try
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
            {
                if (sourceMessageId != null)
                {
                    var existing = await _reservationRepository.GetBySourceMessageIdAsync(workspaceId, sourceMessageId, cancellationToken);
                    if (existing != null) return Result<ReservationDto>.Success(ToDto(existing));
                }
                await _resources.ValidateAsync(resourceVersions, cancellationToken);
                var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, locationId, startTimeUtc, endTimeUtc, null, cancellationToken);
                if (!isAvailable) throw new ConcurrencyException("El horario ya fue tomado por otro cliente.");

                var reservation = Domain.Entities.Reservation.Create(workspaceId, locationId, serviceId, customerIdentifier, customerName, startTimeUtc, endTimeUtc, sourceMessageId);
                created = reservation;
                _reservationRepository.Add(reservation);

                var dto = ToDto(reservation);
                var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_CREATED", Guid.NewGuid().ToString(), $"res_{reservation.Id}", DateTime.UtcNow, dto);
                var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_CREATED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };

                createdEvent = outboxMessage;
                await _outboxRepository.AddAsync(outboxMessage, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                scope.Complete();
                return Result<ReservationDto>.Success(dto);
            }
        }
        catch (Exception ex) when (ex is ConcurrencyException || IsConcurrencyConflict(ex))
        {
            // The ambient transaction has rolled back. Do not leave a losing
            // reservation/event tracked for a later SaveChanges in this scope.
            if (created != null) _unitOfWork.DiscardChanges(created);
            if (createdEvent != null) _unitOfWork.DiscardChanges(createdEvent);
            if (sourceMessageId != null)
            {
                var existing = await _reservationRepository.GetBySourceMessageIdAsync(workspaceId, sourceMessageId, cancellationToken);
                if (existing != null) return Result<ReservationDto>.Success(ToDto(existing));
            }
            return Result<ReservationDto>.Failure(new Error("Reservation.ConcurrencyConflict", "El horario acaba de ser tomado por otro cliente. Por favor, selecciona otro."));
        }
    }

    public Task<Result<ReservationDto>> EditReservationAsync(Guid workspaceId, Guid reservationId, DateTime newDateTime, CancellationToken cancellationToken)
        => EditCoreAsync(workspaceId, reservationId, newDateTime, null, cancellationToken);

    public async Task<Result<ReservationDto>> ApplyExistingOperationAsync(ReservationOperationCommand operation, CancellationToken ct)
    {
        operation.Validate();
        var replay = await ReadOperationAsync(operation, ct);
        if (replay != null) return replay;
        if (operation.Action == ReservationOperationKind.RescheduleExistingReservation && operation.ProposedUtc <= _clock.UtcNow)
            return Result<ReservationDto>.Failure(new Error("Reservation.InvalidTime", "La nueva fecha y hora deben ser futuras."));
        return operation.Action == ReservationOperationKind.RescheduleExistingReservation
            ? await EditCoreAsync(operation.WorkspaceId, operation.ReservationId, operation.ProposedUtc!.Value, operation, ct)
            : await CancelExistingCoreAsync(operation, ct);
    }

    private async Task<Result<ReservationDto>> EditCoreAsync(Guid workspaceId, Guid reservationId, DateTime newDateTime, ReservationOperationCommand? operation, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result<ReservationDto>.Failure(new Error("Reservation.NotFound", "La reserva no existe."));
        if (operation != null && !MatchesOperation(reservation, operation))
            return await ReadOperationAsync(operation, cancellationToken)
                ?? Result<ReservationDto>.Failure(new Error("Reservation.SelectionExpired", "La cita cambió o ya no pertenece al contacto autorizado. Vuelve a consultarla."));
        if (reservation.Status != Domain.Enums.ReservationStatus.Confirmed)
            return Result<ReservationDto>.Failure(new Error("Reservation.InvalidTransition", "Solo se pueden reagendar reservas confirmadas."));

        var resourceVersions = await CaptureResourcesAsync(workspaceId, reservation.LocationId, reservation.ServiceId, cancellationToken);
        var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
        if (!locations.Any(l => l.Id == reservation.LocationId))
            return Result<ReservationDto>.Failure(new Error("Location.NotFound", "La sede seleccionada no existe."));

        var workspaceZone = await GetWorkspaceTimeZoneAsync(workspaceId, cancellationToken);
        if (!TryResolveReservationTime(newDateTime, workspaceZone, out var localDateTime, out var newStartTimeUtc))
            return Result<ReservationDto>.Failure(new Error("Reservation.InvalidTime", "Envía una hora local válida y no ambigua."));

        var businessHours = await _hoursRepository.GetBusinessHoursAsync(workspaceId, reservation.LocationId, cancellationToken);
        var todayHours = businessHours.FirstOrDefault(h => h.DayOfWeek == (int)localDateTime.DayOfWeek);

        if (todayHours == null || todayHours.IsClosed || !TimeSpan.TryParseExact(todayHours.OpenTime, @"hh\:mm", CultureInfo.InvariantCulture, out var openTime) || !TimeSpan.TryParseExact(todayHours.CloseTime, @"hh\:mm", CultureInfo.InvariantCulture, out var closeTime))
            return Result<ReservationDto>.Failure(new Error("Reservation.Closed", "El negocio se encuentra cerrado en el día y horario seleccionado."));

        var timeOnly = localDateTime.TimeOfDay;

        // 🔥 SPRINT 07: Optimizamos. Traemos solo 1 servicio.
        var targetService = await _catalogRepository.GetItemByIdAsync(workspaceId, reservation.ServiceId, cancellationToken) as ServiceDto;

        if (targetService == null) return Result<ReservationDto>.Failure(new Error("Service.NotFound", "El servicio no existe."));
        if (!targetService.IsActive) return Result<ReservationDto>.Failure(new Error("Service.Inactive", "El servicio está inactivo."));
        if (!targetService.RequiresReservation) return Result<ReservationDto>.Failure(new Error("Service.NotReservable", "Servicio no reservable."));
        if (!targetService.DurationInMinutes.HasValue || targetService.DurationInMinutes.Value < 5) return Result<ReservationDto>.Failure(new Error("Service.InvalidDuration", "Duración inválida."));

        if (!_locationAvailabilityService.IsOfferingAvailableAtLocation(targetService, reservation.LocationId))
            return Result<ReservationDto>.Failure(new Error("Service.NotAvailable", "Servicio no disponible en sede."));

        var newEndTimeUtc = newStartTimeUtc.AddMinutes(targetService.DurationInMinutes.Value);
        var localEndTime = TimeZoneInfo.ConvertTimeFromUtc(newEndTimeUtc, workspaceZone);

        if (timeOnly < openTime || localEndTime.Date != localDateTime.Date || localEndTime.TimeOfDay > closeTime)
            return Result<ReservationDto>.Failure(new Error("Reservation.OutOfHours", "Fuera del horario comercial."));

        OutboxMessage? pendingEvent = null;
        try
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
            {
                if (operation != null)
                {
                    var replay = await ReadOperationAsync(operation, cancellationToken);
                    if (replay != null) return replay;
                    if (!await _reservationRepository.HasSingleFutureOwnerAsync(workspaceId, operation.Contact, cancellationToken))
                        return Result<ReservationDto>.Failure(new Error("Reservation.VerificationRequired", "Este contacto requiere verificación adicional con un asesor."));
                }
                await _resources.ValidateAsync(resourceVersions, cancellationToken);
                var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, reservation.LocationId, newStartTimeUtc, newEndTimeUtc, reservation.Id, cancellationToken);
                if (!isAvailable) return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El nuevo horario ya está ocupado."));

                if (operation != null && reservation.StartTime == newStartTimeUtc && reservation.EndTime == newEndTimeUtc)
                    return Result<ReservationDto>.Failure(new Error("Reservation.NoChange", "La cita conserva su fecha y hora; no se realizó ningún cambio."));
                try { reservation.Reschedule(newStartTimeUtc, newEndTimeUtc); }
                catch (DomainException ex) { return Result<ReservationDto>.Failure(new Error("Reservation.InvalidTransition", ex.Message)); }

                var dto = ToDto(reservation);
                var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_RESCHEDULED", Guid.NewGuid().ToString(), $"res_upd_{reservation.Id}", DateTime.UtcNow, dto);
                var outboxMessage = operation == null
                    ? new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_RESCHEDULED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) }
                    : ReservationOperationReceipt.Create(operation, "RESERVATION_RESCHEDULED", dto, dto, _clock.UtcNow);
                pendingEvent = outboxMessage;

                await _outboxRepository.AddAsync(outboxMessage, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                scope.Complete();
                return Result<ReservationDto>.Success(dto);
            }
        }
        catch (Exception ex) when (ex is ConcurrencyException || IsConcurrencyConflict(ex))
        {
            _unitOfWork.DiscardChanges(pendingEvent == null ? [reservation] : [reservation, pendingEvent]);
            if (operation != null)
            {
                var replay = await ReadOperationAsync(operation, cancellationToken);
                if (replay != null) return replay;
            }
            return Result<ReservationDto>.Failure(new Error("Reservation.ConcurrencyConflict", "El horario acaba de ser tomado por otro cliente."));
        }
    }

    public async Task<Result> CancelReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        Domain.Entities.Reservation? changed = null;
        OutboxMessage? pendingEvent = null;
        try
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
            {
                var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
                if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));
    
                reservation.Cancel();
                changed = reservation;
    
                var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_CANCELLED", Guid.NewGuid().ToString(), $"res_can_{reservation.Id}", DateTime.UtcNow, new { ReservationId = reservation.Id, Status = "CANCELLED" });
                var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_CANCELLED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
                pendingEvent = outboxMessage;
    
                await _outboxRepository.AddAsync(outboxMessage, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
    
                scope.Complete();
                return Result.Success();
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error("Reservation.InvalidTransition", ex.Message));
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            if (changed != null) _unitOfWork.DiscardChanges(pendingEvent == null ? [changed] : [changed, pendingEvent]);
            return Result.Failure(new Error("Reservation.ConcurrencyConflict", "La reserva cambió concurrentemente. Vuelve a consultarla."));
        }
    }

    public async Task<Result<Domain.Entities.Reservation>> CancelActiveReservationAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        Domain.Entities.Reservation? changed = null;
        OutboxMessage? pendingEvent = null;
        try
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
            {
                var reservation = await _reservationRepository.GetActiveReservationByPhoneAsync(workspaceId, customerPhone, cancellationToken);
                if (reservation == null) return Result<Domain.Entities.Reservation>.Failure(new Error("Reservation.NotFound", "No tienes ninguna reserva activa."));
    
                reservation.Cancel();
                changed = reservation;
    
                var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_CANCELLED", Guid.NewGuid().ToString(), $"res_can_{reservation.Id}", DateTime.UtcNow, new { ReservationId = reservation.Id, Status = "CANCELLED" });
                var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_CANCELLED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
                pendingEvent = outboxMessage;
    
                await _outboxRepository.AddAsync(outboxMessage, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
    
                scope.Complete();
                return Result<Domain.Entities.Reservation>.Success(reservation);
            }
        }
        catch (DomainException ex)
        {
            return Result<Domain.Entities.Reservation>.Failure(new Error("Reservation.InvalidTransition", ex.Message));
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            if (changed != null) _unitOfWork.DiscardChanges(pendingEvent == null ? [changed] : [changed, pendingEvent]);
            return Result<Domain.Entities.Reservation>.Failure(new Error("Reservation.ConcurrencyConflict", "La reserva cambió concurrentemente. Vuelve a consultarla."));
        }
    }

    public async Task<Result> CompleteReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        Domain.Entities.Reservation? changed = null;
        OutboxMessage? pendingEvent = null;
        try
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
            {
                var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
                if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));
    
                reservation.Complete();
                changed = reservation;
    
                var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_COMPLETED", Guid.NewGuid().ToString(), $"res_comp_{reservation.Id}", DateTime.UtcNow, new { ReservationId = reservation.Id, Status = "COMPLETED" });
                var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_COMPLETED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
                pendingEvent = outboxMessage;
    
                await _outboxRepository.AddAsync(outboxMessage, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
    
                scope.Complete();
                return Result.Success();
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error("Reservation.InvalidTransition", ex.Message));
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            if (changed != null) _unitOfWork.DiscardChanges(pendingEvent == null ? [changed] : [changed, pendingEvent]);
            return Result.Failure(new Error("Reservation.ConcurrencyConflict", "La reserva cambió concurrentemente. Vuelve a consultarla."));
        }
    }
    private bool MatchesOperation(Domain.Entities.Reservation reservation, ReservationOperationCommand operation)
        => reservation.WorkspaceId == operation.WorkspaceId && reservation.CustomerIdentifier == operation.Contact
            && reservation.Status == Domain.Enums.ReservationStatus.Confirmed && reservation.StartTime > _clock.UtcNow
            && reservation.RowVersion == operation.ExpectedVersion && reservation.StartTime == operation.ExpectedStartTime;

    private async Task<Result<ReservationDto>?> ReadOperationAsync(ReservationOperationCommand operation, CancellationToken ct)
    {
        var message = await _outboxRepository.GetByIdAsync(operation.WorkspaceId, operation.Key, ct);
        if (message == null) return null;
        var receipt = ReservationOperationReceipt.Read(message, operation.WorkspaceId, operation.Contact);
        return receipt.Fingerprint == operation.Fingerprint
            ? Result<ReservationDto>.Success(receipt.Result)
            : Result<ReservationDto>.Failure(new Error("Reservation.IdempotencyConflict", "La identidad de esta solicitud ya fue utilizada con otro contenido."));
    }

    private async Task<Result<ReservationDto>> CancelExistingCoreAsync(ReservationOperationCommand operation, CancellationToken ct)
    {
        var reservation = await _reservationRepository.GetByIdAsync(operation.WorkspaceId, operation.ReservationId, ct);
        if (reservation == null || !MatchesOperation(reservation, operation))
            return await ReadOperationAsync(operation, ct)
                ?? Result<ReservationDto>.Failure(new Error("Reservation.SelectionExpired", "La cita cambió o ya no pertenece al contacto autorizado. Vuelve a consultarla."));
        var versions = await CaptureResourcesAsync(operation.WorkspaceId, reservation.LocationId, reservation.ServiceId, ct);
        OutboxMessage? pending = null;
        try
        {
            using var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled);
            var replay = await ReadOperationAsync(operation, ct);
            if (replay != null) return replay;
            if (!await _reservationRepository.HasSingleFutureOwnerAsync(operation.WorkspaceId, operation.Contact, ct))
                return Result<ReservationDto>.Failure(new Error("Reservation.VerificationRequired", "Este contacto requiere verificación adicional con un asesor."));
            await _resources.ValidateAsync(versions, ct);
            reservation.Cancel();
            var dto = ToDto(reservation);
            pending = ReservationOperationReceipt.Create(operation, "RESERVATION_CANCELLED", new { ReservationId = reservation.Id, Status = "CANCELLED" }, dto, _clock.UtcNow);
            await _outboxRepository.AddAsync(pending, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            scope.Complete();
            return Result<ReservationDto>.Success(dto);
        }
        catch (Exception ex) when (ex is ConcurrencyException || IsConcurrencyConflict(ex))
        {
            _unitOfWork.DiscardChanges(pending == null ? [reservation] : [reservation, pending]);
            var replay = await ReadOperationAsync(operation, ct);
            return replay ?? Result<ReservationDto>.Failure(new Error("Reservation.ConcurrencyConflict", "La cita cambió concurrentemente. Vuelve a consultarla."));
        }
    }

    private static ReservationDto ToDto(Domain.Entities.Reservation reservation) => new(
        reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId,
        reservation.CustomerName, reservation.CustomerIdentifier, reservation.StartTime, reservation.Status.ToString());

    private async Task<IReadOnlyList<ReservationResourceStamp>> CaptureResourcesAsync(Guid workspaceId, string locationId, string serviceId, CancellationToken ct) =>
    [
        await _resources.ReadAsync(new(workspaceId, ReservationResourceKind.Location, locationId), ct),
        await _resources.ReadAsync(new(workspaceId, ReservationResourceKind.Service, serviceId), ct)
    ];

    private static bool IsConcurrencyConflict(Exception exception)
    {
        // Npgsql exposes PostgreSQL SQLSTATE through DbException.SqlState;
        // this also covers failures raised when TransactionScope commits.
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException) return true;
            if (current is DbException { SqlState: "40001" or "40P01" or "23505" or "23P01" }) return true;
        }
        return false;
    }
}

