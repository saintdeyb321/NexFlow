using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;

namespace NexFlow.Infrastructure.Gateways;

public class DefaultInstanceResolver : IInstanceResolver
{
    public Task<Guid?> ResolveInstanceAsync(string instanceName, CancellationToken cancellationToken)
    {
        // Temporal: Intentamos parsearlo como Guid directamente.
        // En el futuro, aquí harás un query a la BD: _context.EvolutionInstances.FirstOrDefault(x => x.Name == instanceName)
        if (Guid.TryParse(instanceName, out var workspaceId))
            return Task.FromResult<Guid?>(workspaceId);

        return Task.FromResult<Guid?>(null);
    }
}