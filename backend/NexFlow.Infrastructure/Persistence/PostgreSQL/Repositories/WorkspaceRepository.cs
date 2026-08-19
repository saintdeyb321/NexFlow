using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly NexFlowDbContext _context;

    public WorkspaceRepository(NexFlowDbContext context) => _context = context;

    public void Add(Workspace workspace) => _context.Workspaces.Add(workspace);

    public async Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }
}