namespace NexFlow.Application.Abstractions;

public interface IInstanceResolver
{
    Task<Guid?> ResolveInstanceAsync(string instanceName, CancellationToken cancellationToken);
}