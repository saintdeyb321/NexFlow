using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.SuperAdmin.Workspaces;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

    // 🔥 EL EXTERMINADOR DE DEPENDENCIAS
    public async Task DeleteNuclearAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        // 1. Buscar las membresías asociadas a este workspace ignorando filtros globales
        var memberships = await _context.Memberships
            .IgnoreQueryFilters()
            .Where(m => m.WorkspaceId == workspace.Id)
            .ToListAsync(cancellationToken);

        var userIds = memberships.Select(m => m.UserId).Distinct().ToList();

        // 2. Buscar licencias asociadas y sus módulos de licencia
        var licenses = await _context.Licenses
            .IgnoreQueryFilters()
            .Include(l => l.LicenseModules)
            .Where(l => l.WorkspaceId == workspace.Id)
            .ToListAsync(cancellationToken);

        // 3. Buscar registros condicionales (si existen en el contexto)
        var audits = await _context.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.WorkspaceId == workspace.Id)
            .ToListAsync(cancellationToken);

        var reservations = await _context.Reservations
            .IgnoreQueryFilters()
            .Where(r => r.WorkspaceId == workspace.Id)
            .ToListAsync(cancellationToken);

        // 4. Eliminación limpia de hijos
        if (reservations.Any()) _context.Reservations.RemoveRange(reservations);
        if (audits.Any()) _context.AuditLogs.RemoveRange(audits);

        foreach (var license in licenses)
        {
            if (license.LicenseModules != null && license.LicenseModules.Any())
            {
                _context.LicenseModules.RemoveRange(license.LicenseModules);
            }
        }
        if (licenses.Any()) _context.Licenses.RemoveRange(licenses);

        if (memberships.Any()) _context.Memberships.RemoveRange(memberships);

        // 5. Eliminar el Workspace principal
        _context.Workspaces.Remove(workspace);

        // 6. Lógica inteligente de usuario: 
        // ¿Este usuario ya no tiene ningún otro workspace activo en el sistema? Si es así, se purga también.
        foreach (var userId in userIds)
        {
            var hasOtherWorkspaces = await _context.Memberships
                .IgnoreQueryFilters()
                .AnyAsync(m => m.UserId == userId && m.WorkspaceId != workspace.Id, cancellationToken);

            if (!hasOtherWorkspaces)
            {
                var userToDelete = await _context.Users
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

                if (userToDelete != null)
                {
                    _context.Users.Remove(userToDelete);
                }
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
}