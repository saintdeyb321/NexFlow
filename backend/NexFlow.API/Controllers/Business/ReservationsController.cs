using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Reservations;

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
        IReservationEngine reservationEngine,
        IReservationRepository reservationRepository,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService)
    {
        _reservationEngine = reservationEngine;
        _reservationRepository = reservationRepository;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    private async Task<bool> HasAccessTo(string moduleCode, CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains(moduleCode.ToUpperInvariant());
    }

    [HttpGet]
    public async Task<IActionResult> GetReservations([FromQuery] string locationId, [FromQuery] DateTime date, CancellationToken cancellationToken)
    {
        // 🔥 CORRECCIÓN (Fallo #9): El Guardia de Seguridad de la Licencia
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");
        if (string.IsNullOrEmpty(locationId)) return BadRequest("LocationId es requerido");

        var reservations = await _reservationRepository.GetReservationsForDateAsync(WorkspaceId, locationId, date, cancellationToken);
        return Ok(reservations);
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability([FromQuery] string locationId, [FromQuery] string serviceId, [FromQuery] DateTime date, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");
        if (string.IsNullOrEmpty(locationId) || string.IsNullOrEmpty(serviceId))
            return BadRequest("LocationId y ServiceId son requeridos");

        var slots = await _reservationEngine.GetAvailabilityAsync(WorkspaceId, locationId, serviceId, date, cancellationToken);
        return Ok(slots);
    }

    [HttpPost]
    public async Task<IActionResult> CreateReservation([FromBody] CreateReservationRequest request, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");

        var result = await _reservationEngine.CreateReservationAsync(
            WorkspaceId,
            request.LocationId,
            request.ServiceId,
            request.CustomerIdentifier,
            request.CustomerName,
            request.DateTime,
            cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { Error = result.Error.Description });

        return Ok(result.Value);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> CancelReservation(Guid id, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("RESERVATIONS", cancellationToken)) return StatusCode(403, "Módulo RESERVATIONS no contratado.");

        var result = await _reservationEngine.CancelReservationAsync(WorkspaceId, id, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { Error = result.Error.Description });

        return NoContent();
    }
}

public record CreateReservationRequest(
    string LocationId,
    string ServiceId,
    string CustomerIdentifier,
    string CustomerName,
    DateTime DateTime
);