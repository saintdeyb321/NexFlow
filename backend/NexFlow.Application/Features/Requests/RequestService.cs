using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Requests;

public interface IRequestService
{
    // 🔥 SPRINT 08: Añadimos sourceMessageId a la firma
    Task<string> CreateRequestAsync(Guid workspaceId, string phone, string conversationId, RequestType type, string title, string description, string? sourceMessageId, Dictionary<string, object>? metadata, CancellationToken ct);
}

public class RequestService : IRequestService
{
    private readonly IRequestRepository _requestRepo;
    private readonly INotificationService _notificationService;

    public RequestService(IRequestRepository requestRepo, INotificationService notificationService)
    {
        _requestRepo = requestRepo;
        _notificationService = notificationService;
    }

    public async Task<string> CreateRequestAsync(Guid workspaceId, string phone, string conversationId, RequestType type, string title, string description, string? sourceMessageId, Dictionary<string, object>? metadata, CancellationToken ct)
    {
        // 🔥 SPRINT 08: Idempotencia estricta. Evitamos crear duplicados si el mensaje ya generó un Request.
        if (!string.IsNullOrWhiteSpace(sourceMessageId))
        {
            var latestRequest = await _requestRepo.GetBySourceMessageIdAsync(workspaceId, sourceMessageId, ct);
            if (latestRequest != null)
            {
                await NotifyAsync(workspaceId, latestRequest, ct);
                return latestRequest.Id;
            }
        }

        var newRequest = new RequestRecord
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            ConsumerPhone = phone,
            Type = type,
            Title = title,
            Description = description,
            Status = RequestStatus.Pending,
            SourceMessageId = sourceMessageId,
            Metadata = metadata ?? new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var persisted = await _requestRepo.CreateRequestAsync(workspaceId, newRequest, ct);
        await NotifyAsync(workspaceId, persisted.Request, ct);
        return persisted.Request.Id;
    }

    private Task NotifyAsync(Guid workspaceId, RequestRecord request, CancellationToken ct)
    {
        var notificationType = request.Type == RequestType.CommercialInquiry
            ? NotificationType.NewCommercialRequest
            : NotificationType.SystemAlert;

        return _notificationService.NotifyOnceAsync(
            workspaceId,
            "REQUESTS",
            notificationType,
            request.Id,
            request.Title,
            $"El cliente {request.ConsumerPhone} ha generado una nueva solicitud. Descripción: {request.Description}",
            "/requests",
            ct);
    }
}
