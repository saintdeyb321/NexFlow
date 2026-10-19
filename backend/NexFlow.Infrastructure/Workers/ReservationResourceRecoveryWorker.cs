using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Features.Reservations;
using NexFlow.Infrastructure.Persistence.Firestore;

namespace NexFlow.Infrastructure.Workers;

// Recovery of the durable resource intent itself (not a second business Outbox).
// Attempts can overlap after a crash; Firestore's terminal fence makes this safe.
public sealed class ReservationResourceRecoveryWorker(IServiceScopeFactory scopes, ILogger<ReservationResourceRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RecoverOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Resource recovery cycle failed: {FailureType}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public async Task RecoverOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var resources = scope.ServiceProvider.GetRequiredService<IReservationResourceCoordinator>();
        foreach (var operation in await resources.ClaimPendingAsync(ct))
        {
            try
            {
                if (operation.Key.Kind == ReservationResourceKind.Service)
                    await scope.ServiceProvider.GetRequiredService<FirestoreCatalogRepository>().ReconcileMutationAsync(operation, ct);
                else
                    await scope.ServiceProvider.GetRequiredService<FirestoreLocationRepository>().ReconcileMutationAsync(operation, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning("Resource mutation remains recoverable: {WorkspaceId}/{OperationId} {FailureType}",
                    operation.Key.WorkspaceId, operation.OperationId, ex.GetType().Name);
            }
        }
    }
}
