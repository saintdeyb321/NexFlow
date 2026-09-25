using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Notifications;

namespace NexFlow.API.Controllers.System;

[ApiController]
[Route("api/notifications")]
[Authorize(Policy = "WorkspaceMember")]
public class NotificationController : ControllerBase
{
    private readonly INotificationRepository _notificationRepo;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;

    public NotificationController(
        INotificationRepository notificationRepo,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService)
    {
        _notificationRepo = notificationRepo;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
    }

    [HttpGet]
    public async Task<IActionResult> GetActiveNotifications(CancellationToken cancellationToken)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken);

        // Obtenemos las reales de Firestore
        var rawNotifications = await _notificationRepo.GetUnreadAsync(workspaceId, 50, cancellationToken);

        // 🔥 FILTRO ESTRICTO DE LICENCIA: Solo se envían notificaciones de módulos contratados
        var authorizedNotifications = rawNotifications
            .Where(n => activeModules.Contains(n.ModuleCode.ToUpperInvariant()))
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                ModuleCode = n.ModuleCode,
                Type = n.Type,
                Title = n.Title,
                Message = n.Message,
                IsRead = n.IsRead,
                ActionUrl = n.ActionUrl,
                CreatedAt = n.CreatedAt
            })
            .ToList();

        return Ok(authorizedNotifications);
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id, CancellationToken cancellationToken)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        await _notificationRepo.MarkAsReadAsync(workspaceId, id, cancellationToken);
        return NoContent();
    }
}