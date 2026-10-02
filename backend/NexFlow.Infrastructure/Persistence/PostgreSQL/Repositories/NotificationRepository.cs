using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
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

    public async Task<bool> TryCreateWithCooldownAsync(Guid workspaceId, NotificationRecord record, TimeSpan cooldown, CancellationToken ct)
    {
        // Serialize the check and insert across processes. The notification row
        // itself is the durable cooldown marker, including after it is read.
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"catalog-notification:{workspaceId:N}:{record.ModuleCode}")));
        await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
        var since = DateTime.UtcNow.Subtract(cooldown);
        if (await _context.Notifications.AsNoTracking().AnyAsync(n => n.WorkspaceId == workspaceId &&
            n.ModuleCode == record.ModuleCode && n.Title == record.Title && n.CreatedAt >= since, ct))
        {
            await transaction.CommitAsync(ct);
            return false;
        }
        await CreateAsync(workspaceId, record, ct);
        await transaction.CommitAsync(ct);
        return true;
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
