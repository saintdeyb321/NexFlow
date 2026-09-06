using NexFlow.Application.Features.Requests;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IRequestRepository
{
    Task CreateRequestAsync(Guid workspaceId, RequestRecord request, CancellationToken cancellationToken);
    Task<IEnumerable<RequestRecord>> GetRequestsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task UpdateRequestStatusAsync(Guid workspaceId, string requestId, string status, CancellationToken cancellationToken);

    // 🔥 SPRINT 4: Buscar la última solicitud/trámite activa del cliente por su teléfono
    Task<RequestRecord?> GetLatestRequestByPhoneAsync(Guid workspaceId, string phone, CancellationToken cancellationToken);
}