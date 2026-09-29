using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;

namespace NexFlow.Application.Features.SuperAdmin.Workspaces;
public record DeleteClientCommand(Guid WorkspaceId);
public class DeleteClientCommandHandler(ITenantDeletionScheduler scheduler, ICurrentUser currentUser)
{
    public Task<Result> Handle(DeleteClientCommand request, CancellationToken ct) =>
        scheduler.RequestAsync(request.WorkspaceId, currentUser.UserId, ct);
}
