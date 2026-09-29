using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Workers;
public class TenantDeletionWorker(IServiceScopeFactory scopes, ILogger<TenantDeletionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
                var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
                TenantDeletionJob? job;
                await using (var tx = await db.Database.BeginTransactionAsync(stoppingToken))
                {
                    job = (await db.Set<TenantDeletionJob>().FromSqlInterpolated($"SELECT * FROM \"TenantDeletionJobs\" WHERE (\"Status\" = {(int)TenantDeletionStatus.Pending} AND (\"NextRetryAt\" IS NULL OR \"NextRetryAt\" <= {now})) OR (\"Status\" = {(int)TenantDeletionStatus.Processing} AND (\"LeaseUntil\" IS NULL OR \"LeaseUntil\" <= {now})) ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(stoppingToken)).FirstOrDefault();
                    if (job != null)
                    {
                        job.Status = TenantDeletionStatus.Processing;
                        job.LeaseUntil = now.AddMinutes(6);
                        job.RetryCount++;
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    await tx.CommitAsync(stoppingToken);
                }
                if (job == null) { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); continue; }
                var lease = job.LeaseUntil;
                try
                {
                    if (job.RetryCount > 5) throw new InvalidOperationException("Deletion retry limit reached.");
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(5));
                    var ct = timeout.Token;
                    logger.LogInformation("Tenant deletion requested by {UserId} processing workspace {WorkspaceId}", job.RequestedBy, job.WorkspaceId);
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    await TenantLifecycleLock.AcquireAsync(db, job.WorkspaceId, true, ct);
                    var locked = (await db.Set<TenantDeletionJob>().FromSqlInterpolated($"SELECT * FROM \"TenantDeletionJobs\" WHERE \"WorkspaceId\" = {job.WorkspaceId} FOR UPDATE").AsNoTracking().ToListAsync(ct)).Single();
                    if (locked.LeaseUntil != lease) { await tx.RollbackAsync(ct); continue; }
                    await scope.ServiceProvider.GetRequiredService<ITenantCleanupService>().PurgeTenantDataAsync(job.WorkspaceId, ct);
                    var repo = scope.ServiceProvider.GetRequiredService<IWorkspaceRepository>();
                    var workspace = await repo.GetByIdForSuperAdminAsync(job.WorkspaceId, ct);
                    if (workspace != null) await repo.DeleteNuclearAsync(workspace, ct);
                    job.Status = TenantDeletionStatus.Completed;
                    job.LeaseUntil = null;
                    job.Error = null;
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Deletion failed for {WorkspaceId}", job.WorkspaceId);
                    db.ChangeTracker.Clear();
                    await db.Set<TenantDeletionJob>().Where(j => j.WorkspaceId == job.WorkspaceId && j.LeaseUntil == lease)
                        .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, job.RetryCount >= 5 ? TenantDeletionStatus.Failed : TenantDeletionStatus.Pending)
                            .SetProperty(j => j.NextRetryAt, DateTime.UtcNow.AddSeconds(Math.Min(300, 10 * Math.Pow(2, job.RetryCount))))
                            .SetProperty(j => j.LeaseUntil, (DateTime?)null).SetProperty(j => j.Error, ex.GetType().Name), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tenant deletion cycle failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

