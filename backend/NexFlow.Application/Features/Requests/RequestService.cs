using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.Application.Features.Requests;

public interface IRequestService
{
    // Método antiguo (Mantenido por retrocompatibilidad temporal)
    Task<string> CreateSupportTicketAsync(Guid workspaceId, string phone, string description, CancellationToken ct);

    // 🔥 SPRINT 06: Nuevo método genérico
    Task<string> CreateRequestAsync(Guid workspaceId, string phone, string conversationId, RequestType type, string title, string description, Dictionary<string, object>? metadata, CancellationToken ct);
}

public class RequestService : IRequestService
{
    private readonly IRequestRepository _requestRepo;

    public RequestService(IRequestRepository requestRepo)
    {
        _requestRepo = requestRepo;
    }

    public Task<string> CreateSupportTicketAsync(Guid workspaceId, string phone, string description, CancellationToken ct)
    {
        // Redirige al nuevo sistema marcándolo como Support
        return CreateRequestAsync(workspaceId, phone, "N/A", RequestType.Support, "Solicitud de Atención", description, null, ct);
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
        return newRequest.Id;
    }
}