using NexFlow.Application.Common;
namespace NexFlow.Application.Abstractions;
public interface ITenantDeletionScheduler
{
    Task<Result> RequestAsync(Guid workspaceId, Guid requestedBy, CancellationToken ct);
}
