using NexFlow.Application.Features.SuperAdmin.Workspaces;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions.Repositories;
public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    void Add(Workspace workspace);
    Task<IEnumerable<WorkspaceSummaryDto>> GetAllSummariesAsync(CancellationToken cancellationToken);
}