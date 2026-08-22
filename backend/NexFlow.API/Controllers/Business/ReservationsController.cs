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

    public ReservationsController(
        IReservationEngine reservationEngine,
        IReservationRepository reservationRepository,
        IWorkspaceContext workspaceContext)
    {
        _reservationEngine = reservationEngine;
        _reservationRepository = reservationRepository;
        _workspaceContext = workspaceContext;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    // 1. Obtener Reservas para pintar el Calendario de React
    [HttpGet]
    public async Task<IActionResult> GetReservations([FromQuery] string locationId, [FromQuery] DateTime date, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(locationId)) return BadRequest("LocationId es requerido");

        var reservations = await _reservationRepository.GetReservationsForDateAsync(WorkspaceId, locationId, date, cancellationToken);
        return Ok(reservations);
    }

    // 2. Consultar Disponibilidad Manual (Si la secretaria quiere agendar a alguien en persona)
    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability([FromQuery] string locationId, [FromQuery] string serviceId, [FromQuery] DateTime date, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(locationId) || string.IsNullOrEmpty(serviceId))
            return BadRequest("LocationId y ServiceId son requeridos");

        var slots = await _reservationEngine.GetAvailabilityAsync(WorkspaceId, locationId, serviceId, date, cancellationToken);
        return Ok(slots);
    }

    [HttpPost]
    public async Task<IActionResult> CreateReservation([FromBody] CreateReservationRequest request, CancellationToken cancellationToken)
    {
        var result = await _reservationEngine.CreateReservationAsync(
            WorkspaceId,
            request.LocationId,
            request.ServiceId,
            request.CustomerIdentifier,
            request.CustomerName, // <--- 1ro: EL NOMBRE
            request.DateTime,     // <--- 2do: LA FECHA
            cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { Error = result.Error.Description });

        return Ok(result.Value);
    }

    // 4. Cancelar Reserva
    [HttpDelete("{id}")]
    public async Task<IActionResult> CancelReservation(Guid id, CancellationToken cancellationToken)
    {
        var result = await _reservationEngine.CancelReservationAsync(WorkspaceId, id, cancellationToken);

        if (result.IsFailure)
            return BadRequest(new { Error = result.Error.Description });

        return NoContent();
    }
}

// DTO Auxiliar para recibir el POST del frontend
// DTO Auxiliar para recibir el POST del frontend
public record CreateReservationRequest(
    string LocationId,
    string ServiceId,
    string CustomerIdentifier,
    string CustomerName, // <--- FALTABA AGREGARLO AQUÍ
    DateTime DateTime
);