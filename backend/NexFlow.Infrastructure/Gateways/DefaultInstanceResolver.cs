using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Gateways;

public class DefaultInstanceResolver : IInstanceResolver
{
    private readonly NexFlowDbContext _dbContext;

    // Inyectamos el contexto de BD para consultas rápidas sin pasar por repositorios de dominio
    public DefaultInstanceResolver(NexFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid?> ResolveInstanceAsync(string instanceName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceName)) return null;

        // 🔥 SPRINT 1.2: Búsqueda real del ID del negocio mediante el nombre de su instancia en Evolution
        var normalized = EvolutionInstanceIdentity.LegacyAlias(instanceName);
        var matches = await _dbContext.Set<NexFlow.Domain.Entities.Workspace>()
            .AsNoTracking()
            .Where(w => w.Status != NexFlow.Domain.Enums.WorkspaceStatus.Deleting && w.EvolutionInstanceName != null
                && (w.EvolutionInstanceName == instanceName || w.EvolutionInstanceName.Replace("-", "").Replace(" ", "").ToLower() == normalized))
            .Select(w => new { w.Id, w.EvolutionInstanceName }).Take(2).ToListAsync(cancellationToken);
        if (matches.Count != 1) return null;
        var match = matches[0];
        if (match.EvolutionInstanceName == instanceName) return match.Id;
        if (EvolutionInstanceIdentity.LegacyAlias(match.EvolutionInstanceName!) != instanceName) return null;
        // An authenticated inbound alias is evidence of the actual provider identity. Repair routing once.
        var changed = await _dbContext.Workspaces.Where(w => w.Id == match.Id && w.EvolutionInstanceName == match.EvolutionInstanceName)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, instanceName), cancellationToken);
        return changed == 1 ? match.Id : null;
    }

    public async Task<string?> GetInstanceNameAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        // 🔥 Viaje de vuelta: Obtener el string de la instancia para armar la URL de salida
        var workspace = await _dbContext.Set<NexFlow.Domain.Entities.Workspace>()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace?.EvolutionInstanceName is not { } name || workspace.Status == NexFlow.Domain.Enums.WorkspaceStatus.Deleting) return null;
        var owner = await ResolveInstanceAsync(name, cancellationToken);
        return owner == workspaceId ? name : null;
    }
}
