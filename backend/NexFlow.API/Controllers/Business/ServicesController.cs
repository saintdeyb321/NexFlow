using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Application.Features.Business;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/services")]
[Authorize(Policy = "WorkspaceMember")]
public class ServicesController : ControllerBase
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;
    private readonly IReservationRepository _reservations;

    public ServicesController(
        ICatalogRepository catalogRepository,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService,
        IReservationRepository reservations)
    {
        _catalogRepository = catalogRepository;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
        _reservations = reservations;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    private async Task<bool> HasAccessToServices(CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains("SERVICES");
    }

    [HttpGet]
    public async Task<IActionResult> GetServices([FromQuery] string? locationId, CancellationToken cancellationToken)
    {
        if (!await HasAccessToServices(cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");

        var allItems = await _catalogRepository.GetItemsByTypeAsync(WorkspaceId, "SERVICE", cancellationToken);
        var services = allItems.Where(i => i.Type == "SERVICE").OfType<ServiceDto>();

        if (!string.IsNullOrWhiteSpace(locationId))
        {
            services = services.Where(p =>
                string.Equals(p.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase) ||
                (p.LocationIds != null && p.LocationIds.Contains(locationId))
            );
        }

        return Ok(services.ToList());
    }

    [HttpPost]
    public async Task<IActionResult> SaveService([FromBody] ServiceDto service, CancellationToken cancellationToken)
    {
        if (!await HasAccessToServices(cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");

        if (service.Type != "SERVICE") return BadRequest(new { message = "Tipo de offering inválido." });
        service.Id = Guid.NewGuid().ToString();
        try { await _catalogRepository.SaveItemAsync(WorkspaceId, service, true, cancellationToken); }
        catch (NexFlow.Domain.Exceptions.DomainException ex) { return BadRequest(new { message = ex.Message }); }


        return Ok(service);
    }

    [HttpPut("{serviceId}")]
    public async Task<IActionResult> UpdateService(string serviceId, [FromBody] ServiceDto service, CancellationToken cancellationToken)
    {
        if (!await HasAccessToServices(cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");
        if (service.Type != "SERVICE") return BadRequest(new { message = "Tipo de offering inválido." });
        service.Id = serviceId;
        var existing = await _catalogRepository.GetItemByIdAsync(WorkspaceId, serviceId, cancellationToken);
        if (existing == null) return NotFound(new { message = "Servicio no encontrado." });
        if (existing is not ServiceDto previous) return StatusCode(403, "Este endpoint solo permite actualizar servicios.");
        var reservedLocations = await _reservations.GetFutureConfirmedServiceLocationsAsync(WorkspaceId, serviceId, cancellationToken);
        var scope = service.LocationScope?.Trim().ToUpperInvariant();
        var retainedLocations = (service.LocationIds ?? []).Select(id => id?.Trim()).ToHashSet(StringComparer.Ordinal);
        if (reservedLocations.Count > 0 && (!service.IsActive || !service.RequiresReservation
            || service.DurationInMinutes != previous.DurationInMinutes
            || (scope != "ALL" && reservedLocations.Any(id => !retainedLocations.Contains(id)))))
            return Conflict(new { code = "Service.FutureReservations", message = "No puedes modificar la disponibilidad o duración de un servicio con reservas futuras confirmadas." });
        try { await _catalogRepository.SaveItemAsync(WorkspaceId, service, false, cancellationToken); }
        catch (NexFlow.Domain.Exceptions.DomainException ex) { return BadRequest(new { message = ex.Message }); }
        return Ok(service);
    }

    [HttpDelete("{serviceId}")]
    public async Task<IActionResult> DeleteService(string serviceId, CancellationToken cancellationToken)
    {
        if (!await HasAccessToServices(cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");

        var item = await _catalogRepository.GetItemByIdAsync(WorkspaceId, serviceId, cancellationToken);
        if (item != null && item.Type != "SERVICE")
            return StatusCode(403, "Este endpoint solo permite eliminar servicios.");
        if (item != null)
        {
            // This guard protects committed reservations. It is not a cross-store transaction;
            // a booking already validating in Firestore can still commit concurrently in SQL.
            if ((await _reservations.GetFutureConfirmedServiceLocationsAsync(WorkspaceId, serviceId, cancellationToken)).Count > 0)
                return Conflict(new { code = "Service.FutureReservations", message = "No puedes desactivar un servicio con reservas futuras confirmadas." });
            await _catalogRepository.DeleteItemAsync(WorkspaceId, serviceId, cancellationToken);
        }

        return NoContent();
    }

}
