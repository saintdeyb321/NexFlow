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

public class TemplateRepository : ITemplateRepository
{
    private readonly NexFlowDbContext _context;

    public TemplateRepository(NexFlowDbContext context) => _context = context;

    public void Add(Template template) => _context.Templates.Add(template);

    public async Task<Template?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Templates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<Template?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await _context.Templates
            .FirstOrDefaultAsync(t => t.Name.ToLower() == name.ToLower(), cancellationToken);
    }
    public async Task<Template?> GetByCodeAsync(string templateCode, CancellationToken cancellationToken)
    {
        return await _context.Templates
            .FirstOrDefaultAsync(t => t.Code == templateCode.ToUpper(), cancellationToken);
    }
    public async Task<IEnumerable<Template>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _context.Templates.ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Module>> GetActiveModulesForTemplateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        return await _context.TemplateModules
            .Where(tm => tm.TemplateId == templateId)
            .Join(_context.Modules,
                tm => tm.ModuleId,
                m => m.Id,
                (tm, m) => m)
            .Where(m => m.Status == ModuleStatus.Active)
            .ToListAsync(cancellationToken);
    }
}