using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.Application.Features.Notifications;

public interface INotificationService
{
    Task NotifyAsync(Guid workspaceId, string moduleCode, NotificationType type, string title, string message, string? actionUrl, CancellationToken ct);
}

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;

    public NotificationService(INotificationRepository repository)
    {
        _repository = repository;
    }

    public async Task NotifyAsync(Guid workspaceId, string moduleCode, NotificationType type, string title, string message, string? actionUrl, CancellationToken ct)
    {
        var notification = new NotificationRecord
        {
            ModuleCode = moduleCode.ToUpperInvariant(),
            Type = type,
            Title = title,
            Message = message,
            ActionUrl = actionUrl,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.CreateAsync(workspaceId, notification, ct);
    }
}