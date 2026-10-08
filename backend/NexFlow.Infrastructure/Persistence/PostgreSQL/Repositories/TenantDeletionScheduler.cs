using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
public class TenantDeletionScheduler(NexFlowDbContext db, IEntitlementService entitlements, Microsoft.Extensions.Logging.ILogger<TenantDeletionScheduler> logger) : ITenantDeletionScheduler
{
    public async Task<Result> RequestAsync(Guid workspaceId, Guid requestedBy, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await TenantLifecycleLock.AcquireAsync(db, workspaceId, true, ct);
        var workspace = (await db.Workspaces.FromSqlInterpolated($"SELECT * FROM \"Workspaces\" WHERE \"Id\" = {workspaceId} FOR UPDATE").ToListAsync(ct)).FirstOrDefault();
        if (workspace != null && await db.WhatsAppConnections.AnyAsync(c => c.WorkspaceId == workspaceId && c.OperationUntil > DateTime.UtcNow, ct))
            throw new NexFlow.Domain.Exceptions.ConcurrencyException("Espera a que termine la operación de WhatsApp antes de eliminar el workspace.");
        var job = await db.Set<TenantDeletionJob>().FindAsync(new object[] { workspaceId }, ct);
        if (workspace == null) return job?.Status == TenantDeletionStatus.Completed ? Result.Success() : Result.Failure(new Error("Workspace.NotFound", "El negocio no existe."));
        workspace.BeginDeletion();
        if (job == null) db.Add(new TenantDeletionJob { WorkspaceId = workspaceId, RequestedBy = requestedBy });
        else if (job.Status == TenantDeletionStatus.Failed)
        {
            job.Status = TenantDeletionStatus.Pending;
            job.RetryCount = 0;
            job.NextRetryAt = null;
            job.Error = null;
            job.RequestedBy = requestedBy;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, "Tenant deletion scheduled: UserId {UserId}, WorkspaceId {WorkspaceId}", requestedBy, workspaceId);
        entitlements.InvalidateWorkspaceCache(workspaceId);
        return Result.Success();
    }
}
