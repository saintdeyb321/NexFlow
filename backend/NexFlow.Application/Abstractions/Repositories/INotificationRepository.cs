using NexFlow.Application.Features.Notifications;

namespace NexFlow.Application.Abstractions.Repositories;

public interface INotificationRepository
{
    Task CreateAsync(Guid workspaceId, NotificationRecord notification, CancellationToken cancellationToken);
    Task<IEnumerable<NotificationRecord>> GetUnreadAsync(Guid workspaceId, int limit, CancellationToken cancellationToken);
    Task MarkAsReadAsync(Guid workspaceId, string notificationId, CancellationToken cancellationToken);
}