namespace NexFlow.Application.Abstractions;

public interface IInstanceResolver
{
    // Webhook entrante: Evolution -> NexFlow
    Task<Guid?> ResolveInstanceAsync(string instanceName, CancellationToken cancellationToken);

    // 🔥 Mensaje saliente: NexFlow -> Evolution
    Task<string?> GetInstanceNameAsync(Guid workspaceId, CancellationToken cancellationToken);
}