using Microsoft.EntityFrameworkCore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Entities.System;
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
                var message = await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().ClaimAsync(stoppingToken);
                if (message == null) { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); continue; }
                await ProcessClaimedAsync(scope.ServiceProvider, message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox cycle failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public async Task ProcessClaimedAsync(IServiceProvider provider, OutboxMessage message, CancellationToken ct)
    {
        var repo = provider.GetRequiredService<IOutboxRepository>();
        try
        {
            if (message.RetryCount > 5) throw new JsonException("Outbox attempt limit exhausted.");
            var payload = JsonSerializer.Deserialize<N8nEventPayload<JsonElement>>(message.PayloadJson);
            if (payload == null || payload.Data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || payload.WorkspaceId != message.WorkspaceId || string.IsNullOrWhiteSpace(payload.IdempotencyKey) || string.IsNullOrWhiteSpace(payload.EventType) || payload.EventType != message.EventType)
                throw new JsonException("Invalid Outbox payload.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var db = provider.GetRequiredService<NexFlowDbContext>();
            await using var lifecycle = await db.Database.BeginTransactionAsync(timeout.Token);
            await TenantLifecycleLock.AcquireAsync(db, message.WorkspaceId, false, timeout.Token);
            if (!await db.Workspaces.AnyAsync(w => w.Id == message.WorkspaceId && w.Status != NexFlow.Domain.Enums.WorkspaceStatus.Deleting, timeout.Token))
                throw new JsonException("Workspace is not available.");
            if (message.EventType == "CATALOG_GENERATION_REQUESTED")
            {
                if (payload.Data.ValueKind != JsonValueKind.Object
                    || !payload.Data.TryGetProperty("Scope", out var scopeValue) || scopeValue.ValueKind != JsonValueKind.String
                    || !payload.Data.TryGetProperty("GenerationId", out var idValue) || idValue.ValueKind != JsonValueKind.String
                    || !payload.Data.TryGetProperty("SourceHash", out var hashValue) || hashValue.ValueKind != JsonValueKind.String)
                    throw new JsonException("Invalid catalog generation identity.");
                var scope = scopeValue.GetString();
                var generationId = idValue.GetString();
                if (scope is not ("PRODUCT" or "SERVICE") || string.IsNullOrWhiteSpace(generationId)
                    || payload.CorrelationId != generationId || payload.IdempotencyKey != $"catalog_{generationId}")
                    throw new JsonException("Invalid catalog generation identity.");
                await provider.GetRequiredService<ICatalogGenerationService>().ReconcileGenerationAsync(message.WorkspaceId, scope, timeout.Token);
                var artifact = await provider.GetRequiredService<ICatalogArtifactRepository>().GetCurrentArtifactAsync(message.WorkspaceId, scope, timeout.Token);
                if (artifact == null || artifact.WorkspaceId != message.WorkspaceId || artifact.Scope != scope
                    || artifact.Status != CatalogArtifactStatus.Generating || artifact.GenerationId != generationId || artifact.SourceHash != hashValue.GetString())
                {
                    // Canceled/expired/superseded events cannot start a workflow.
                    await repo.FinishAsync(message, true, null, false, timeout.Token);
                    await lifecycle.CommitAsync(timeout.Token);
                    return;
                }
            }
            var webhookId = configuration["N8n:EventsWebhookId"];
            if (string.IsNullOrWhiteSpace(webhookId)) throw new InvalidOperationException("Missing configuration: N8n:EventsWebhookId.");
            await provider.GetRequiredService<IWorkflowGateway>().TriggerWorkflowAsync(webhookId, payload, timeout.Token);
            await repo.FinishAsync(message, true, null, false, timeout.Token);
            await lifecycle.CommitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox {EventId} failed for workspace {WorkspaceId}", message.Id, message.WorkspaceId);
            await repo.FinishAsync(message, false, ex.GetType().Name, ex is JsonException, ct);
            if (message.EventType != "CATALOG_GENERATION_REQUESTED") return;
            // A stale lease cannot fail an artifact: verify SQL's terminal CAS result.
            var failed = await provider.GetRequiredService<NexFlowDbContext>().OutboxMessages.AsNoTracking()
                .SingleOrDefaultAsync(m => m.Id == message.Id && m.WorkspaceId == message.WorkspaceId && m.Status == OutboxStatus.Failed, ct);
            if (failed == null) return;
            try { await provider.GetRequiredService<ICatalogGenerationService>().ReconcileFailedDeliveryAsync(failed, ct); }
            catch (Exception recovery) when (!ct.IsCancellationRequested)
            {
                // Persisted dead letters are retried by the recovery worker if Firestore is unavailable.
                logger.LogError(recovery, "Catalog delivery recovery deferred for {WorkspaceId}/{EventId}", message.WorkspaceId, message.Id);
            }
        }
    }
}
