
namespace NexFlow.Application.Abstractions;

public interface ITenantCleanupService
{
    Task PurgeTenantDataAsync(Guid workspaceId, CancellationToken cancellationToken);
}