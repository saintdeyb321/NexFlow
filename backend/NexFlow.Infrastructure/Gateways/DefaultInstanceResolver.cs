using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
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
        var workspace = await _dbContext.Set<NexFlow.Domain.Entities.Workspace>()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.EvolutionInstanceName == instanceName, cancellationToken);

        return workspace?.Id;
    }

    public async Task<string?> GetInstanceNameAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        // 🔥 Viaje de vuelta: Obtener el string de la instancia para armar la URL de salida
        var workspace = await _dbContext.Set<NexFlow.Domain.Entities.Workspace>()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        return workspace?.EvolutionInstanceName;
    }
}