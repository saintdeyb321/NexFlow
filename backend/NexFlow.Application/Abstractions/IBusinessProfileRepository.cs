using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Abstractions;

public interface IBusinessProfileRepository
{
    Task<BusinessProfileDto?> GetProfileAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveProfileAsync(Guid workspaceId, BusinessProfileDto profile, CancellationToken cancellationToken);
}