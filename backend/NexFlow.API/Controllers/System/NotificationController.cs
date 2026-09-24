using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Notifications;

namespace NexFlow.API.Controllers.System;

[ApiController]
[Route("api/notifications")]
[Authorize(Policy = "WorkspaceMember")]
public class NotificationController : ControllerBase
{
    // Reemplaza INotificationRepository con tu interfaz real de BD cuando la crees
    // private readonly INotificationRepository _notificationRepo; 
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;

    public NotificationController(
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService)
    {
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
    }

    [HttpGet]
    public async Task<IActionResult> GetActiveNotifications(CancellationToken cancellationToken)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken);

        // TODO: var rawNotifications = await _notificationRepo.GetUnreadAsync(workspaceId, cancellationToken);
        var rawNotifications = new List<NotificationDto>(); // Mock temporal

        // 🔥 FILTRO ESTRICTO DE LICENCIA: Solo se envían notificaciones de módulos contratados
        var authorizedNotifications = rawNotifications
            .Where(n => activeModules.Contains(n.ModuleCode.ToUpperInvariant()))
            .OrderByDescending(n => n.CreatedAt)
            .ToList();

        return Ok(authorizedNotifications);
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id, CancellationToken cancellationToken)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        // TODO: await _notificationRepo.MarkAsReadAsync(workspaceId, id, cancellationToken);
        return NoContent();
    }
}