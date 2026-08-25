using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.SuperAdmin.Workspaces;
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

    public async Task<IEnumerable<WorkspaceSummaryDto>> GetAllSummariesAsync(CancellationToken cancellationToken)
    {
        // 1. Ejecutamos la consulta y la traemos a memoria PRIMERO
        var queryResults = await (from w in _context.Workspaces
                                  join m in _context.Memberships on w.Id equals m.WorkspaceId
                                  join u in _context.Users on m.UserId equals u.Id
                                  orderby w.CreatedAt descending
                                  select new { Workspace = w, User = u })
                                  .ToListAsync(cancellationToken);

        // 2. Mapeamos el DTO en memoria. C# maneja la conversión de Enum a int perfectamente.
        return queryResults.Select(x => new WorkspaceSummaryDto
        {
            Id = x.Workspace.Id,
            Name = x.Workspace.Name,
            Status = (int)x.Workspace.Status,
            OwnerEmail = x.User.Email.Value,
            CreatedAt = x.Workspace.CreatedAt
        });
    }
}