using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class ModuleRepository : IModuleRepository
{
    private readonly NexFlowDbContext _context;

    public ModuleRepository(NexFlowDbContext context) => _context = context;

    public async Task<Module?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Set<Module>().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Module>> GetActiveModulesAsync(IEnumerable<Guid> moduleIds, CancellationToken cancellationToken)
    {
        return await _context.Set<Module>()
            .Where(m => moduleIds.Contains(m.Id) && m.Status == ModuleStatus.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Module>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var upperCodes = codes.Select(c => c.ToUpperInvariant()).ToList();

        return await _context.Set<Module>()
            .Where(m => upperCodes.Contains(m.Code) && m.Status == ModuleStatus.Active)
            .ToListAsync(cancellationToken);
    }
}