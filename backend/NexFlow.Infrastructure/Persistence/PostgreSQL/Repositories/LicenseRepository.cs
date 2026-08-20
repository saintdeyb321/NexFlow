using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class LicenseRepository : ILicenseRepository
{
    private readonly NexFlowDbContext _context;

    public LicenseRepository(NexFlowDbContext context)
    {
        _context = context;
    }

    public void Add(License license)
    {
        _context.Licenses.Add(license);
    }

    public async Task<License?> GetByWorkspaceIdAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        return await _context.Licenses
            .Include(l => l.LicenseModules) // EF Core cargará los módulos automáticamente
            .FirstOrDefaultAsync(l => l.WorkspaceId == workspaceId, cancellationToken);
    }
}