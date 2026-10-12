using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Workers;

public sealed class CatalogGenerationRecoveryWorker(IServiceScopeFactory scopes, ILogger<CatalogGenerationRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Catalog generation recovery cycle failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        // Enumerate existing durable workspaces: no new Firestore collection-group indexes or SQL schema.
        for (var offset = 0; ; offset += 100)
        {
            List<Guid> workspaces;
            using (var scope = scopes.CreateScope())
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                workspaces = await scope.ServiceProvider.GetRequiredService<NexFlowDbContext>().Workspaces.AsNoTracking()
                    .Where(w => w.Status != NexFlow.Domain.Enums.WorkspaceStatus.Deleting).OrderBy(w => w.Id)
                    .Skip(offset).Take(100).Select(w => w.Id).ToListAsync(timeout.Token);
            }
            foreach (var workspaceId in workspaces)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var db = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
                    await using var lifecycle = await db.Database.BeginTransactionAsync(timeout.Token);
                    await TenantLifecycleLock.AcquireAsync(db, workspaceId, false, timeout.Token);
                    if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId && w.Status != NexFlow.Domain.Enums.WorkspaceStatus.Deleting, timeout.Token)) continue;
                    var service = scope.ServiceProvider.GetRequiredService<ICatalogGenerationService>();
                    // Retry recovery if Firestore was unavailable when SQL recorded a definitive delivery failure.
                    var since = DateTime.UtcNow.AddDays(-1);
                    var failed = await db.OutboxMessages.AsNoTracking().Where(m => m.WorkspaceId == workspaceId
                        && m.EventType == "CATALOG_GENERATION_REQUESTED" && m.Status == OutboxStatus.Failed && m.CreatedAt >= since)
                        .OrderByDescending(m => m.CreatedAt).Take(50).ToListAsync(timeout.Token);
                    foreach (var message in failed) await service.ReconcileFailedDeliveryAsync(message, timeout.Token);
                    foreach (var target in new[] { "PRODUCT", "SERVICE" }) await service.ReconcileGenerationAsync(workspaceId, target, timeout.Token);
                    await lifecycle.CommitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogError(ex, "Catalog generation recovery deferred for {WorkspaceId}", workspaceId); }
            }
            if (workspaces.Count < 100) return;
        }
    }
}
