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
        // Hacemos el JOIN a través de la tabla Membership para encontrar a los usuarios
        var query = from w in _context.Workspaces
                    join m in _context.Memberships on w.Id equals m.WorkspaceId
                    join u in _context.Users on m.UserId equals u.Id
                    // Si tienes un rol definido en Membership (ej: IsOwner o Role == Owner), 
                    // puedes agregarlo aquí. Si el creador es el único miembro inicial, esto basta:
                    select new WorkspaceSummaryDto
                    {
                        Id = w.Id,
                        Name = w.Name,
                        Status = (int)w.Status,
                        OwnerEmail = u.Email.Value, // <-- Solución al Value Object: extraemos el string
                        CreatedAt = w.CreatedAt
                    };

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}