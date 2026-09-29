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

    public void Remove(Workspace workspace) => _context.Workspaces.Remove(workspace);

    public async Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Workspaces.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<Workspace?> GetByIdForSuperAdminAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Workspaces
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task DeleteNuclearAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var memberships = await _context.Memberships.IgnoreQueryFilters().Where(m => m.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);
        var userIds = memberships.Select(m => m.UserId).Distinct().ToList();

        var licenses = await _context.Licenses.IgnoreQueryFilters().Include(l => l.LicenseModules).Where(l => l.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);

        var audits = await _context.AuditLogs.IgnoreQueryFilters().Where(a => a.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);
        var reservations = await _context.Reservations.IgnoreQueryFilters().Where(r => r.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);
        var notifications = await _context.Notifications.IgnoreQueryFilters().Where(n => n.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);

        // 🔥 Reemplazado ProcessedMessages por InboundMessages
        var inboundMsgs = await _context.InboundMessages.IgnoreQueryFilters().Where(p => p.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);

        if (reservations.Any()) _context.Reservations.RemoveRange(reservations);
        if (audits.Any()) _context.AuditLogs.RemoveRange(audits);
        if (notifications.Any()) _context.Notifications.RemoveRange(notifications);
        if (inboundMsgs.Any()) _context.InboundMessages.RemoveRange(inboundMsgs);
        var outbox = await _context.OutboxMessages.Where(m => m.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);
        _context.OutboxMessages.RemoveRange(outbox);

        foreach (var license in licenses)
        {
            if (license.LicenseModules != null && license.LicenseModules.Any())
            {
                _context.LicenseModules.RemoveRange(license.LicenseModules);
            }
        }
        if (licenses.Any()) _context.Licenses.RemoveRange(licenses);
        if (memberships.Any()) _context.Memberships.RemoveRange(memberships);

        _context.Workspaces.Remove(workspace);

        foreach (var userId in userIds)
        {
            var hasOtherWorkspaces = await _context.Memberships
                .IgnoreQueryFilters()
                .AnyAsync(m => m.UserId == userId && m.WorkspaceId != workspace.Id, cancellationToken);

            if (!hasOtherWorkspaces && !await _context.SystemAdministrators.AnyAsync(a => a.UserId == userId, cancellationToken))
            {
                var userToDelete = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (userToDelete != null) _context.Users.Remove(userToDelete);
            }
        }
    }

    public async Task<IEnumerable<WorkspaceSummaryDto>> GetAllSummariesAsync(CancellationToken cancellationToken)
    {
        var queryResults = await (from w in _context.Workspaces.IgnoreQueryFilters()
                                  join m in _context.Memberships.IgnoreQueryFilters() on w.Id equals m.WorkspaceId
                                  join u in _context.Users.IgnoreQueryFilters() on m.UserId equals u.Id
                                  orderby w.CreatedAt descending
                                  select new { Workspace = w, User = u })
                                  .ToListAsync(cancellationToken);

        return queryResults.Select(x => new WorkspaceSummaryDto
        {
            Id = x.Workspace.Id,
            Name = x.Workspace.Name,
            Status = (int)x.Workspace.Status,
            OwnerEmail = x.User.Email.Value,
            CreatedAt = x.Workspace.CreatedAt
        });
    }

    public async Task<Guid?> GetIdByEvolutionInstanceNameAsync(string instanceName, CancellationToken cancellationToken)
    {
        var workspace = await _context.Set<Workspace>()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.EvolutionInstanceName == instanceName, cancellationToken);

        return workspace?.Id;
    }

    public async Task<string?> GetEvolutionInstanceNameByIdAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var workspace = await _context.Set<Workspace>()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        return workspace?.EvolutionInstanceName;
    }
}
