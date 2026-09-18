using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.Application.Features.Requests;

public interface IRequestService
{
    Task<string> CreateSupportTicketAsync(Guid workspaceId, string phone, string description, CancellationToken ct);
}

public class RequestService : IRequestService
{
    private readonly IRequestRepository _requestRepo;

    public RequestService(IRequestRepository requestRepo)
    {
        _requestRepo = requestRepo;
    }

    public async Task<string> CreateSupportTicketAsync(Guid workspaceId, string phone, string description, CancellationToken ct)
    {
        var newRequest = new RequestRecord
        {
            Id = Guid.NewGuid().ToString(),
            ConsumerPhone = phone,
            Title = "Solicitud de Atención",
            Description = description,
            Status = RequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _requestRepo.CreateRequestAsync(workspaceId, newRequest, ct);
        return newRequest.Id;
    }
}