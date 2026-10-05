using Microsoft.EntityFrameworkCore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace NexFlow.Infrastructure.Workers;

public class OutboxProcessorWorker(IServiceScopeFactory scopes, ILogger<OutboxProcessorWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var message = await repo.ClaimAsync(stoppingToken);
                if (message == null) { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); continue; }
                try
                {
                    if (message.RetryCount > 5) throw new JsonException("Outbox attempt limit exhausted.");
                    var payload = JsonSerializer.Deserialize<N8nEventPayload<JsonElement>>(message.PayloadJson);
                    if (payload == null || payload.Data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || payload.WorkspaceId != message.WorkspaceId || string.IsNullOrWhiteSpace(payload.IdempotencyKey) || string.IsNullOrWhiteSpace(payload.EventType) || payload.EventType != message.EventType)
                        throw new JsonException("Invalid Outbox payload.");
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var db = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
                    await using var lifecycle = await db.Database.BeginTransactionAsync(timeout.Token);
                    await TenantLifecycleLock.AcquireAsync(db, message.WorkspaceId, false, timeout.Token);
                    if (!await db.Workspaces.AnyAsync(w => w.Id == message.WorkspaceId && w.Status != NexFlow.Domain.Enums.WorkspaceStatus.Deleting, timeout.Token))
                        throw new JsonException("Workspace is not available.");
                    var webhookId = configuration["N8n:EventsWebhookId"];
                    if (string.IsNullOrWhiteSpace(webhookId)) throw new InvalidOperationException("Missing configuration: N8n:EventsWebhookId.");
                    await scope.ServiceProvider.GetRequiredService<IWorkflowGateway>().TriggerWorkflowAsync(webhookId, payload, timeout.Token);
                    await repo.FinishAsync(message, true, null, false, timeout.Token);
                    await lifecycle.CommitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Outbox {EventId} failed for workspace {WorkspaceId}", message.Id, message.WorkspaceId);
                    await repo.FinishAsync(message, false, ex.GetType().Name, ex is JsonException, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox cycle failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
