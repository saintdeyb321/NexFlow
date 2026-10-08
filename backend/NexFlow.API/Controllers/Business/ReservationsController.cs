using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Reservations;
using System.Globalization;

namespace NexFlow.API.Controllers.Reservations;

[ApiController]
[Route("api/reservations")]
[Authorize(Policy = "WorkspaceMember")]
public class ReservationsController : ControllerBase
{
    private readonly IReservationEngine _reservationEngine;
    private readonly IReservationRepository _reservationRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;

    public ReservationsController(
        IReservationEngine reservationEngine, IReservationRepository reservationRepository,
        IWorkspaceContext workspaceContext, IEntitlementService entitlementService)
    {
        _reservationEngine = reservationEngine; _reservationRepository = reservationRepository;
        _workspaceContext = workspaceContext; _entitlementService = entitlementService;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    private async Task<bool> HasAccessTo(string moduleCode, CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains(moduleCode.ToUpperInvariant());
    }

    [HttpGet]
    public async Task<IActionResult> GetReservations(
        [FromQuery] string? locationId,
        [FromQuery] DateTime? date,
        [FromServices] IBusinessProfileRepository profileRepo,
        [FromServices] ILocationRepository locationRepo,
        CancellationToken cancellationToken,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WorkspaceId == Guid.Empty) return StatusCode(403, new { code = "Security.WorkspaceRequired", message = "Workspace autenticado requerido." });
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");
        if (!await _entitlementService.HasCapabilityAccessAsync(WorkspaceId, "RESERVATIONS", "READ", cancellationToken))
            return StatusCode(403, new { code = "Security.CapabilityDenied", message = "No tienes permiso para consultar reservas." });
        if (string.IsNullOrWhiteSpace(locationId)) return BadRequest(new { code = "Validation.Error", message = "LocationId es requerido; usa 'all' para todas las sedes." });

        var query = HttpContext?.Request.Query;
        if (query != null && new[] { "locationId", "date", "from", "to" }.Any(key => query.TryGetValue(key, out var values) && values.Count > 1))
            return BadRequest(new { code = "Validation.Error", message = "Cada parámetro de consulta debe aparecer una sola vez." });
        var hasDate = date.HasValue || query?.ContainsKey("date") == true;
        var hasRange = from != null || to != null || query?.ContainsKey("from") == true || query?.ContainsKey("to") == true;
        if (hasDate && hasRange)
            return BadRequest(new { code = "Validation.Error", message = "Usa date o from/to, sin combinarlos." });

        DateOnly firstDate, endDate;
        if (hasRange)
        {
            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out firstDate)
                || !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out endDate))
                return BadRequest(new { code = "Validation.Error", message = "from y to son obligatorios y deben tener formato YYYY-MM-DD." });
            var days = endDate.DayNumber - firstDate.DayNumber;
            if (days < 1 || days > 7)
                return BadRequest(new { code = "Validation.Error", message = "El intervalo [from,to) debe abarcar de 1 a 7 días; to es exclusivo." });
        }
        else
        {
            if (!date.HasValue)
                return BadRequest(new { code = "Validation.Error", message = "Indica date o un intervalo from/to." });
            firstDate = DateOnly.FromDateTime(date.Value);
            if (firstDate == DateOnly.MaxValue)
                return BadRequest(new { code = "Validation.Error", message = "date no permite calcular el límite del día siguiente." });
            endDate = firstDate.AddDays(1);
        }

        // 'all' is an explicit workspace aggregate, including historical locations. A concrete location must belong to it.
        var selectedLocation = string.Equals(locationId, "all", StringComparison.OrdinalIgnoreCase) ? null : locationId;
        if (selectedLocation != null)
        {
            var locations = await locationRepo.GetLocationsAsync(WorkspaceId, cancellationToken);
            if (!locations.Any(location => location.Id == selectedLocation))
                return NotFound(new { code = "Location.NotFound", message = "La sede no existe en este workspace." });
        }

        var profile = await profileRepo.GetProfileAsync(WorkspaceId, cancellationToken);
        var tzId = string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone;

        TimeZoneInfo workspaceZone;
        try { workspaceZone = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch (TimeZoneNotFoundException) { workspaceZone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
        catch (InvalidTimeZoneException) { workspaceZone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }

        DateTime startUtc, endUtc;
        try
        {
            startUtc = CivilBoundaryUtc(firstDate, workspaceZone);
            endUtc = CivilBoundaryUtc(endDate, workspaceZone);
        }
        catch (ArgumentException)
        {
            return BadRequest(new { code = "Validation.Error", message = "No se pueden resolver los límites de esas fechas en la zona horaria del negocio." });
        }

        var reservations = await _reservationRepository.GetReservationsForDateAsync(WorkspaceId, selectedLocation, startUtc, endUtc, cancellationToken);
        return Ok(reservations.Select(reservation => new ReservationDto(
            reservation.Id, reservation.WorkspaceId, reservation.LocationId, reservation.ServiceId,
            reservation.CustomerName, reservation.CustomerIdentifier, reservation.StartTime, reservation.Status.ToString())));
    }

    private static DateTime CivilBoundaryUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            // Some zones advance at midnight. Find the first real instant at the zone data's second precision.
            var invalid = local;
            var valid = local.AddDays(1);
            if (zone.IsInvalidTime(valid)) throw new ArgumentException("Límite civil no resoluble.");
            while (valid.Ticks - invalid.Ticks > 1)
            {
                var middle = new DateTime(invalid.Ticks + (valid.Ticks - invalid.Ticks) / 2, DateTimeKind.Unspecified);
                if (zone.IsInvalidTime(middle)) invalid = middle;
                else valid = middle;
            }
            local = valid;
            // TimeZoneInfo can expose an inclusive transition endpoint as xx:xx:59.999 on Windows.
            var fraction = local.Ticks % TimeSpan.TicksPerSecond;
            if (fraction != 0) local = local.AddTicks(TimeSpan.TicksPerSecond - fraction);
        }
        // A repeated midnight starts at its first occurrence, so no reservations of that civil date are omitted.
        if (zone.IsAmbiguousTime(local)) return DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability([FromQuery] string locationId, [FromQuery] string serviceId, [FromQuery] DateTime date, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");
        if (string.IsNullOrEmpty(locationId) || string.IsNullOrEmpty(serviceId)) return BadRequest(new { code = "Validation.Error", message = "LocationId y ServiceId son requeridos" });

        var slots = await _reservationEngine.GetAvailabilityAsync(WorkspaceId, locationId, serviceId, date, cancellationToken);
        return Ok(slots);
    }

    [HttpPost]
    public async Task<IActionResult> CreateReservation(
        [FromBody] CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");

        // 🔥 SPRINT 2: Sin zona = hora del workspace; con Z = UTC. Solo el motor convierte.
        var result = await _reservationEngine.CreateReservationAsync(
            WorkspaceId, request.LocationId, request.ServiceId, request.CustomerIdentifier, request.CustomerName, request.DateTime, cancellationToken);

        if (result.IsFailure) return ReservationError(result.Error);
        return Ok(result.Value);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> EditReservation(Guid id, [FromBody] EditReservationRequest request, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");

        // 🔥 SPRINT 2: La edición usa el mismo contrato temporal que la creación.
        var result = await _reservationEngine.EditReservationAsync(WorkspaceId, id, request.NewDateTime, cancellationToken);

        if (result.IsFailure) return ReservationError(result.Error);
        return Ok(result.Value);
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateReservationStatus(Guid id, [FromBody] UpdateReservationStatusRequest request, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");

        if (string.Equals(request.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            var result = await _reservationEngine.CompleteReservationAsync(WorkspaceId, id, cancellationToken);
            if (result.IsFailure) return ReservationError(result.Error);
            return Ok();
        }

        return BadRequest(new { code = "Validation.Error", message = "Solo se permite el estado 'Completed' a través de este endpoint." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> CancelReservation(Guid id, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");

        var result = await _reservationEngine.CancelReservationAsync(WorkspaceId, id, cancellationToken);

        if (result.IsFailure) return ReservationError(result.Error);
        return NoContent();
    }
    private IActionResult ReservationError(NexFlow.Application.Common.Error error)
    {
        var status = error.Code.EndsWith(".NotFound", StringComparison.Ordinal) ? 404
            : error.Code is "Reservation.Conflict" or "Reservation.ConcurrencyConflict" or "Reservation.InvalidTransition" ? 409 : 400;
        return StatusCode(status, new { code = error.Code, message = error.Description });
    }
}

public record CreateReservationRequest(string LocationId, string ServiceId, string CustomerIdentifier, string CustomerName, DateTime DateTime);
public record EditReservationRequest(DateTime NewDateTime);
public record UpdateReservationStatusRequest(string Status);

