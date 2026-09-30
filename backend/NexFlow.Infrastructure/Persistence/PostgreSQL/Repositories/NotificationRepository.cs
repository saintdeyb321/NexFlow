using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly NexFlowDbContext _context;

    public NotificationRepository(NexFlowDbContext context)
    {
        _context = context;
    }

    public async Task CreateAsync(Guid workspaceId, NotificationRecord record, CancellationToken ct)
    {
        var notification = Notification.Create(
            workspaceId,
            record.ModuleCode,
            record.Type.ToString(),
            record.Title,
            record.Message,
            record.ActionUrl
        );

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<NotificationRecord>> GetUnreadAsync(Guid workspaceId, int limit, CancellationToken ct)
    {
        var notifications = await _context.Notifications
            .Where(n => n.WorkspaceId == workspaceId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return notifications.Select(Map);
    }

    public async Task<NotificationRecord?> GetByIdAsync(Guid workspaceId, string notificationId, CancellationToken ct)
    {
        if (!Guid.TryParse(notificationId, out var id)) return null;
        var notification = await _context.Notifications.AsNoTracking()
            .SingleOrDefaultAsync(n => n.WorkspaceId == workspaceId && n.Id == id, ct);
        return notification == null ? null : Map(notification);
    }

    private static NotificationRecord Map(Notification n) => new()
        {
            Id = n.Id.ToString(),
            ModuleCode = n.ModuleCode,
            Type = Enum.TryParse<NotificationType>(n.Type, out var parsedType) ? parsedType : NotificationType.SystemAlert,
            Title = n.Title,
            Message = n.Message,
            ActionUrl = n.ActionUrl,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt
        };

    public async Task MarkAsReadAsync(Guid workspaceId, string id, CancellationToken ct)
    {
        if (Guid.TryParse(id, out var guidId))
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.WorkspaceId == workspaceId && n.Id == guidId, ct);

            if (notification != null)
            {
                notification.MarkAsRead();
                await _context.SaveChangesAsync(ct);
            }
        }
    }
}
