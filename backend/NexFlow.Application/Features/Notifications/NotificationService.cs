using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.Application.Features.Notifications;

public interface INotificationService
{
    Task NotifyAsync(Guid workspaceId, string moduleCode, NotificationType type, string title, string message, string? actionUrl, CancellationToken ct);
    Task NotifyCatalogUnavailableAsync(Guid workspaceId, string scope, CancellationToken ct);
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

    public async Task NotifyCatalogUnavailableAsync(Guid workspaceId, string scope, CancellationToken ct)
    {
        if (scope is not ("PRODUCT" or "SERVICE")) throw new ArgumentException("Invalid catalog scope.", nameof(scope));
        var products = scope == "PRODUCT";
        await _repository.TryCreateWithCooldownAsync(workspaceId, new NotificationRecord
        {
            ModuleCode = products ? "CATALOG" : "SERVICES", Type = NotificationType.SystemAlert,
            Title = "Catálogo PDF pendiente de actualización",
            Message = $"Un consumidor solicitó el catálogo de {(products ? "productos" : "servicios")} y no existe un PDF CURRENT válido. Revisa y actualiza el catálogo.",
            ActionUrl = products ? "/catalog" : "/services", CreatedAt = DateTime.UtcNow
        }, TimeSpan.FromHours(6), ct);
    }
}
