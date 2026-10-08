using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;

namespace NexFlow.Infrastructure.Workers;

public sealed class CatalogUploadCleanupWorker(IServiceScopeFactory scopes, ILogger<CatalogUploadCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<ICatalogUploadRepository>();
                var service = scope.ServiceProvider.GetRequiredService<ICatalogGenerationService>();
                using var queryTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                queryTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                var operations = await repository.GetDueCleanupAsync(DateTime.UtcNow, 50, queryTimeout.Token);
                foreach (var operation in operations)
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(30));
                        await service.ReconcileUploadAsync(operation, timeout.Token);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex) { logger.LogError(ex, "PDF cleanup deferred for {WorkspaceId}/{Scope}/{UploadId}", operation.WorkspaceId, operation.Scope, operation.Id); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "PDF upload reconciliation cycle failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
