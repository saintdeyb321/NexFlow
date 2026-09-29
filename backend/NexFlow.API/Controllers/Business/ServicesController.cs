using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Application.Features.Business;
using NexFlow.API.Services.BackgroundServices;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/services")]
[Authorize(Policy = "WorkspaceMember")]
public class ServicesController : ControllerBase
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;
    private readonly IBackgroundTaskQueue _taskQueue;

    public ServicesController(
        ICatalogRepository catalogRepository,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService,
        IBackgroundTaskQueue taskQueue)
    {
        _catalogRepository = catalogRepository;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
        _taskQueue = taskQueue;
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

        var allItems = await _catalogRepository.GetItemsAsync(WorkspaceId, cancellationToken);
        var services = allItems.Where(i => i.Type == "SERVICE");

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

        // 🔥 SPRINT 09: Validaciones de Integridad del Servicio
        if (string.IsNullOrWhiteSpace(service.Name))
            return BadRequest(new { message = "El nombre del servicio es obligatorio." });

        if (service.PriceMinorUnits < 0)
            return BadRequest(new { message = "El precio no puede ser negativo." });

        if (string.IsNullOrWhiteSpace(service.Currency))
            return BadRequest(new { message = "La moneda del precio es obligatoria (ej. PEN, USD)." });

        if (service.RequiresReservation)
        {
            if (!service.DurationInMinutes.HasValue || service.DurationInMinutes <= 0)
                return BadRequest(new { message = "Un servicio que requiere reserva debe tener una duración válida mayor a 0 minutos." });
        }

        service.LocationScope = service.LocationScope?.Trim().ToUpperInvariant() ?? "ALL";
        if (service.LocationScope == "SPECIFIC" && (service.LocationIds == null || !service.LocationIds.Any()))
            return BadRequest(new { message = "Si el alcance es SPECIFIC, debes proveer al menos una sede (LocationIds)." });

        if (service.Type != "SERVICE")
            return BadRequest(new { message = "Este endpoint solo admite entidades SERVICE." });

        if (string.IsNullOrWhiteSpace(service.Id)) service.Id = Guid.NewGuid().ToString();
        else
        {
            var existing = await _catalogRepository.GetItemByIdAsync(WorkspaceId, service.Id, cancellationToken);
            if (existing != null && existing.Type != "SERVICE")
                return StatusCode(403, "No se permite sobrescribir una entidad de otro tipo.");
        }

        if (string.IsNullOrEmpty(service.CategoryId))
        {
            service.CategoryId = Guid.Empty.ToString();
        }
        else
        {
            var category = await _catalogRepository.GetCategoryByIdAsync(WorkspaceId, service.CategoryId, cancellationToken);
            if (category == null) return BadRequest(new { message = "La categoría asignada no existe." });
            if (category.Scope != "SERVICE" && category.Scope != "SHARED")
                return BadRequest(new { message = "El servicio requiere una categoría SERVICE o SHARED." });
        }

        await _catalogRepository.SaveItemAsync(WorkspaceId, service, cancellationToken);

        QueueArtifactInvalidation(WorkspaceId);

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
            await _catalogRepository.DeleteItemAsync(WorkspaceId, serviceId, cancellationToken);
            QueueArtifactInvalidation(WorkspaceId);
        }

        return NoContent();
    }

    private void QueueArtifactInvalidation(Guid workspaceId)
    {
        _taskQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, token) =>
        {
            using var scope = serviceProvider.CreateScope();
            var generationService = scope.ServiceProvider.GetRequiredService<ICatalogGenerationService>();
            await generationService.CheckAndInvalidateStaleArtifactsAsync(workspaceId, token);
        });
    }
}