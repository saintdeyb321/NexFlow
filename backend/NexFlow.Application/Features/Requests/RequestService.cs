using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Requests;

public interface IRequestService
{
    // 🔥 SPRINT 11 (Auditoría): Eliminado el método CreateSupportTicketAsync() legacy
    Task<string> CreateRequestAsync(Guid workspaceId, string phone, string conversationId, RequestType type, string title, string description, Dictionary<string, object>? metadata, CancellationToken ct);
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

    public async Task<string> CreateRequestAsync(Guid workspaceId, string phone, string conversationId, RequestType type, string title, string description, Dictionary<string, object>? metadata, CancellationToken ct)
    {
        var newRequest = new RequestRecord
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            ConsumerPhone = phone,
            Type = type,
            Title = title,
            Description = description,
            Status = RequestStatus.Pending,
            Metadata = metadata ?? new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _requestRepo.CreateRequestAsync(workspaceId, newRequest, ct);

        var notificationType = type == RequestType.CommercialInquiry
            ? NotificationType.NewCommercialRequest
            : NotificationType.SystemAlert;

        await _notificationService.NotifyAsync(
            workspaceId,
            "REQUESTS",
            notificationType,
            title,
            $"El cliente {phone} ha generado una nueva solicitud. Descripción: {description}",
            "/requests",
            ct);

        return newRequest.Id;
    }
}