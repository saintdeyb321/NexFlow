using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Abstractions.Integrations;

public interface IEvolutionConnectionService
{
    Task<string> GetConnectionStatusAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<string?> ConnectAndGetQrAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<bool> DisconnectAsync(Guid workspaceId, CancellationToken cancellationToken);
}