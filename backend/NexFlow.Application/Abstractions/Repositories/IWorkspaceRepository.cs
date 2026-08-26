using NexFlow.Application.Features.SuperAdmin.Workspaces;
using NexFlow.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Workspace?> GetByIdForSuperAdminAsync(Guid id, CancellationToken cancellationToken);
    void Add(Workspace workspace);
    void Remove(Workspace workspace);

    // 🔥 NUEVO: Método para destruir en cascada
    Task DeleteNuclearAsync(Workspace workspace, CancellationToken cancellationToken);

    Task<IEnumerable<WorkspaceSummaryDto>> GetAllSummariesAsync(CancellationToken cancellationToken);
}