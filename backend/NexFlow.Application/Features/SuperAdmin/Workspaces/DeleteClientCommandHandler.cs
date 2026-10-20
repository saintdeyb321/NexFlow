using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;

namespace NexFlow.Application.Features.SuperAdmin.Workspaces;
public record DeleteClientCommand(Guid WorkspaceId);
public class DeleteClientCommandHandler(ITenantDeletionScheduler scheduler, ICurrentUser currentUser)
{
    public Task<Result> Handle(DeleteClientCommand request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return TenantDeletionSafety.IsEnabled
            ? scheduler.RequestAsync(request.WorkspaceId, currentUser.UserId, ct)
            : Task.FromResult(Result.Failure(TenantDeletionSafety.Unavailable));
    }
}
