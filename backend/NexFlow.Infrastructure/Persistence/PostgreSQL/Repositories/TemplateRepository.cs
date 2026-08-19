using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

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

    public async Task<IEnumerable<Module>> GetActiveModulesForTemplateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        // Unimos TemplateModule con Module para devolver solo los módulos que están ACTIVOS
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