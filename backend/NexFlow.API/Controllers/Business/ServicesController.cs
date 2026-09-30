using Microsoft.EntityFrameworkCore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
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

        var allItems = await _catalogRepository.GetItemsByTypeAsync(WorkspaceId, "SERVICE", cancellationToken);
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

        if (service.Type != "SERVICE") return BadRequest(new { message = "Tipo de offering inválido." });
        service.Id = Guid.NewGuid().ToString();
        try { await _catalogRepository.SaveItemAsync(WorkspaceId, service, true, cancellationToken); }
        catch (NexFlow.Domain.Exceptions.DomainException ex) { return BadRequest(new { message = ex.Message }); }

        QueueArtifactInvalidation(WorkspaceId);

        return Ok(service);
    }

    [HttpPut("{serviceId}")]
    public async Task<IActionResult> UpdateService(string serviceId, [FromBody] ServiceDto service, CancellationToken cancellationToken)
    {
        if (!await HasAccessToServices(cancellationToken)) return StatusCode(403, "Módulo SERVICES no contratado.");
        if (service.Type != "SERVICE") return BadRequest(new { message = "Tipo de offering inválido." });
        service.Id = serviceId;
        try { await _catalogRepository.SaveItemAsync(WorkspaceId, service, false, cancellationToken); }
        catch (NexFlow.Domain.Exceptions.DomainException ex) { return BadRequest(new { message = ex.Message }); }
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
            var lifecycleDb = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
            await using var lifecycle = await lifecycleDb.Database.BeginTransactionAsync(token);
            await TenantLifecycleLock.AcquireAsync(lifecycleDb, workspaceId, false, token);
            if (!await lifecycleDb.Workspaces.AnyAsync(w => w.Id == workspaceId && w.Status != NexFlow.Domain.Enums.WorkspaceStatus.Deleting, token)) return;
            var generationService = scope.ServiceProvider.GetRequiredService<ICatalogGenerationService>();
            await generationService.CheckAndInvalidateStaleArtifactsAsync(workspaceId, token);
        });
    }
}
