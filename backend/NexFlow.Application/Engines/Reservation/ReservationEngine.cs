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

    public ReservationEngine(
        IReservationRepository reservationRepository,
        ICatalogRepository catalogRepository,
        IBusinessHoursRepository hoursRepository,
        IBusinessProfileRepository profileRepository,
        ILocationRepository locationRepository,
        IUnitOfWork unitOfWork,
        ILocationAvailabilityService locationAvailabilityService,
        IClock clock,
        IOutboxRepository outboxRepository)
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

    public async Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(Guid workspaceId, string locationId, string serviceId, DateTime date, CancellationToken cancellationToken)
    {
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

            bool isOccupied = existingReservations.Any(r => r.Status != Domain.Enums.ReservationStatus.Cancelled && r.StartTime < utcSlotEnd && r.EndTime > utcSlotStart);
            bool isPast = currentSlotStartLocal <= localNow;

            if (!isOccupied && !isPast) availableSlots.Add(new TimeSlotDto(currentSlotStartLocal, currentSlotEndLocal, true));
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
                var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, locationId, startTimeUtc, endTimeUtc, null, cancellationToken);
                if (!isAvailable) throw new ConcurrencyException("El horario ya fue tomado por otro cliente.");

                var reservation = Domain.Entities.Reservation.Create(workspaceId, locationId, serviceId, customerIdentifier, customerName, startTimeUtc, endTimeUtc, sourceMessageId);
                created = reservation;
                _reservationRepository.Add(reservation);

                var dto = new ReservationDto(reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId, reservation.CustomerIdentifier, reservation.CustomerName, reservation.StartTime, reservation.Status.ToString());
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

    public async Task<Result<ReservationDto>> EditReservationAsync(Guid workspaceId, Guid reservationId, DateTime newDateTime, CancellationToken cancellationToken)
    {
        var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
        if (reservation == null) return Result<ReservationDto>.Failure(new Error("Reservation.NotFound", "La reserva no existe."));
        if (reservation.Status != Domain.Enums.ReservationStatus.Confirmed)
            return Result<ReservationDto>.Failure(new Error("Reservation.InvalidTransition", "Solo se pueden reagendar reservas confirmadas."));

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

        try
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
            {
                var isAvailable = await _reservationRepository.IsTimeSlotAvailableAsync(workspaceId, reservation.LocationId, newStartTimeUtc, newEndTimeUtc, reservation.Id, cancellationToken);
                if (!isAvailable) return Result<ReservationDto>.Failure(new Error("Reservation.Conflict", "El nuevo horario ya está ocupado."));

                try { reservation.Reschedule(newStartTimeUtc, newEndTimeUtc); }
                catch (DomainException ex) { return Result<ReservationDto>.Failure(new Error("Reservation.InvalidTransition", ex.Message)); }

                var dto = new ReservationDto(reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId, reservation.CustomerIdentifier, reservation.CustomerName, reservation.StartTime, reservation.Status.ToString());
                var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_RESCHEDULED", Guid.NewGuid().ToString(), $"res_upd_{reservation.Id}", DateTime.UtcNow, dto);
                var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_RESCHEDULED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };

                await _outboxRepository.AddAsync(outboxMessage, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                scope.Complete();
                return Result<ReservationDto>.Success(dto);
            }
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            return Result<ReservationDto>.Failure(new Error("Reservation.ConcurrencyConflict", "El horario acaba de ser tomado por otro cliente."));
        }
    }

    public async Task<Result> CancelReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        try
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
            {
                var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
                if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));
    
                reservation.Cancel();
    
                var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_CANCELLED", Guid.NewGuid().ToString(), $"res_can_{reservation.Id}", DateTime.UtcNow, new { ReservationId = reservation.Id, Status = "CANCELLED" });
                var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_CANCELLED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
    
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
            return Result.Failure(new Error("Reservation.ConcurrencyConflict", "La reserva cambió concurrentemente. Vuelve a consultarla."));
        }
    }

    public async Task<Result<Domain.Entities.Reservation>> CancelActiveReservationAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken)
    {
        try
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
            {
                var reservation = await _reservationRepository.GetActiveReservationByPhoneAsync(workspaceId, customerPhone, cancellationToken);
                if (reservation == null) return Result<Domain.Entities.Reservation>.Failure(new Error("Reservation.NotFound", "No tienes ninguna reserva activa."));
    
                reservation.Cancel();
    
                var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_CANCELLED", Guid.NewGuid().ToString(), $"res_can_{reservation.Id}", DateTime.UtcNow, new { ReservationId = reservation.Id, Status = "CANCELLED" });
                var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_CANCELLED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
    
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
            return Result<Domain.Entities.Reservation>.Failure(new Error("Reservation.ConcurrencyConflict", "La reserva cambió concurrentemente. Vuelve a consultarla."));
        }
    }

    public async Task<Result> CompleteReservationAsync(Guid workspaceId, Guid reservationId, CancellationToken cancellationToken)
    {
        try
        {
            using (var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable, Timeout = TimeSpan.FromSeconds(15) }, TransactionScopeAsyncFlowOption.Enabled))
            {
                var reservation = await _reservationRepository.GetByIdAsync(workspaceId, reservationId, cancellationToken);
                if (reservation == null) return Result.Failure(new Error("Reservation.NotFound", "La reserva no existe."));
    
                reservation.Complete();
    
                var payload = new N8nEventPayload<object>(workspaceId, "RESERVATION_COMPLETED", Guid.NewGuid().ToString(), $"res_comp_{reservation.Id}", DateTime.UtcNow, new { ReservationId = reservation.Id, Status = "COMPLETED" });
                var outboxMessage = new OutboxMessage { WorkspaceId = workspaceId, EventType = "RESERVATION_COMPLETED", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) };
    
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
            return Result.Failure(new Error("Reservation.ConcurrencyConflict", "La reserva cambió concurrentemente. Vuelve a consultarla."));
        }
    }
    private static ReservationDto ToDto(Domain.Entities.Reservation reservation) => new(
        reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId,
        reservation.CustomerIdentifier, reservation.CustomerName, reservation.StartTime, reservation.Status.ToString());

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

