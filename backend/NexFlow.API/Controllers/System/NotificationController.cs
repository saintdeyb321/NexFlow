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
    private readonly ICurrentUser _currentUser;
    private readonly ISystemAdministratorRepository _systemAdministrators;

    public NotificationController(
        INotificationRepository notificationRepo,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService,
        ICurrentUser currentUser,
        ISystemAdministratorRepository systemAdministrators)
    {
        _notificationRepo = notificationRepo;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
        _currentUser = currentUser;
        _systemAdministrators = systemAdministrators;
    }

    [HttpGet]
    public async Task<IActionResult> GetActiveNotifications(CancellationToken cancellationToken)
    {
        var workspaceId = _workspaceContext.CurrentWorkspaceId;
        var capabilities = await _entitlementService.GetEffectiveCapabilitiesAsync(workspaceId, cancellationToken);
        var isSuperAdmin = _currentUser.UserId != Guid.Empty &&
            await _systemAdministrators.IsUserSuperAdminAsync(_currentUser.UserId, cancellationToken);

        var rawNotifications = await _notificationRepo.GetUnreadAsync(workspaceId, 50, cancellationToken);

        var authorizedNotifications = rawNotifications
            .Where(n => isSuperAdmin ||
                (capabilities.TryGetValue(n.ModuleCode.ToUpperInvariant(), out var allowed) && allowed.Contains("READ")))
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
        var notification = await _notificationRepo.GetByIdAsync(workspaceId, id, cancellationToken);
        if (notification == null) return NotFound();
        if (!await _entitlementService.HasCapabilityAccessAsync(workspaceId, notification.ModuleCode, "READ", cancellationToken))
            return StatusCode(403, new { code = "Security.CapabilityDenied", message = "No tienes permiso para leer esta notificación." });
        await _notificationRepo.MarkAsReadAsync(workspaceId, id, cancellationToken);
        return NoContent();
    }
}
