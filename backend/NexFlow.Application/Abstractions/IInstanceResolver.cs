namespace NexFlow.Application.Abstractions;

public interface IInstanceResolver
{
    // Webhook entrante: Evolution -> NexFlow
    Task<Guid?> ResolveInstanceAsync(string instanceName, CancellationToken cancellationToken);

    // Only a webhook that already passed provider authentication may confirm a legacy alias.
    Task<Guid?> ResolveAuthenticatedInstanceAsync(string instanceName, CancellationToken cancellationToken);

    // 🔥 Mensaje saliente: NexFlow -> Evolution
    Task<string?> GetInstanceNameAsync(Guid workspaceId, CancellationToken cancellationToken);
}
