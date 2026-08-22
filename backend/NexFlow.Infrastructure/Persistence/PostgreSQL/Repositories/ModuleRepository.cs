using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class ModuleRepository : IModuleRepository
{
    private readonly NexFlowDbContext _context;

    public ModuleRepository(NexFlowDbContext context) => _context = context;

    public void Add(Module module) => _context.Modules.Add(module);

    public async Task<Module?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Modules.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Module>> GetAllActiveAsync(CancellationToken cancellationToken)
    {
        return await _context.Modules
            .Where(m => m.Status == ModuleStatus.Active)
            .ToListAsync(cancellationToken);
    }

    // Busca una lista de códigos inmutables (Para el Custom Provisioning)
    public async Task<List<Module>> GetByCodesAsync(IEnumerable<string> moduleCodes, CancellationToken cancellationToken)
    {
        var upperCodes = moduleCodes.Select(c => c.ToUpper()).ToList();

        return await _context.Modules
            .Where(m => upperCodes.Contains(m.Code) && m.Status == ModuleStatus.Active)
            .ToListAsync(cancellationToken);
    }

    // EL MÉTODO QUE FALTABA: Busca módulos activos por una lista de IDs
    public async Task<IEnumerable<Module>> GetActiveModulesAsync(IEnumerable<Guid> moduleIds, CancellationToken cancellationToken)
    {
        return await _context.Modules
            .Where(m => moduleIds.Contains(m.Id) && m.Status == ModuleStatus.Active)
            .ToListAsync(cancellationToken);
    }
}