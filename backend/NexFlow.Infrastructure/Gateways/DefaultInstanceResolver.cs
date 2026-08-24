using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.Infrastructure.Gateways;

public class DefaultInstanceResolver : IInstanceResolver
{
    private readonly IWorkspaceRepository _workspaceRepository;

    public DefaultInstanceResolver(IWorkspaceRepository workspaceRepository)
    {
        _workspaceRepository = workspaceRepository;
    }

    public async Task<Guid?> ResolveInstanceAsync(string instanceName, CancellationToken cancellationToken)
    {
        // 🔥 SPRINT 5: Validación explícita contra la base de datos.
        // Ya no adivinamos. Si el nombre de instancia no es un negocio válido, lo rechazamos de inmediato.
        if (Guid.TryParse(instanceName, out var workspaceId))
        {
            var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);

            if (workspace != null)
            {
                return workspace.Id;
            }
        }

        return null;
    }
}