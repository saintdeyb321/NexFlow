using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.Application.Features.SuperAdmin.Workspaces;

public class GetSystemWorkspacesQueryHandler
{
    private readonly IWorkspaceRepository _workspaceRepository;

    public GetSystemWorkspacesQueryHandler(IWorkspaceRepository workspaceRepository)
    {
        _workspaceRepository = workspaceRepository;
    }

    public async Task<IEnumerable<WorkspaceSummaryDto>> Handle(CancellationToken cancellationToken)
    {
        return await _workspaceRepository.GetAllSummariesAsync(cancellationToken);
    }
}