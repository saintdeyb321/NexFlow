using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Features.Reservations;
using NexFlow.Infrastructure.Persistence.Firestore;
using Google.Cloud.Firestore;
using NexFlow.Application.Common;

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
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                await resources.ExecuteRecoveryAsync(operation.Key.WorkspaceId, async () =>
                {
                    if (operation.Key.Kind == ReservationResourceKind.Service)
                        await scope.ServiceProvider.GetRequiredService<FirestoreCatalogRepository>().ReconcileMutationAsync(operation, timeout.Token, throwOnRejected: false);
                    else
                        await scope.ServiceProvider.GetRequiredService<FirestoreLocationRepository>().ReconcileMutationAsync(operation, timeout.Token);
                }, timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception invalid) when (invalid is InvalidReservationResourceMutationException or NexFlow.Domain.Exceptions.DomainException or KeyNotFoundException)
            {
                try
                {
                    // A rejected terminal fence arbitrates against any old valid
                    // attempt before SQL is unlocked. Do not clear an intent alone.
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    await resources.ExecuteRecoveryAsync(operation.Key.WorkspaceId, async () =>
                        await new ReservationResourceFence(scope.ServiceProvider.GetRequiredService<FirestoreDb>(), resources)
                            .ApplyAsync(operation, _ => Task.FromResult(Result.Failure(new Error("Resource.InvalidPayload", "La operación requiere revisión administrativa."))), timeout.Token), timeout.Token);
                    logger.LogWarning("Invalid resource mutation sealed: {WorkspaceId}/{OperationId}", operation.Key.WorkspaceId, operation.OperationId);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning("Invalid resource mutation remains blocked: {WorkspaceId}/{OperationId} {FailureType}",
                        operation.Key.WorkspaceId, operation.OperationId, ex.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Resource mutation remains recoverable: {WorkspaceId}/{OperationId} {FailureType}",
                    operation.Key.WorkspaceId, operation.OperationId, ex.GetType().Name);
            }
        }
    }
}
