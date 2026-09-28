using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using System.Text.Json;

namespace NexFlow.Infrastructure.Workers;

public class OutboxProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorWorker> _logger;

    public OutboxProcessorWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Processor Worker iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fatal en el ciclo del Outbox Processor.");
            }

            // Pausa de 10 segundos antes de buscar nuevos mensajes
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
        var workflowGateway = scope.ServiceProvider.GetRequiredService<IWorkflowGateway>();

        var pendingMessages = await outboxRepo.GetPendingMessagesAsync(20, cancellationToken);

        foreach (var msg in pendingMessages)
        {
            // 🔥 SPRINT 11: Bloqueo Transaccional EF Core puro (Cero colisiones de Workers)
            using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);

            try
            {
                // Intentamos reclamar el mensaje. FOR UPDATE SKIP LOCKED asegura que si otro worker ya lo tomó, este query devuelve null.
                var lockedMessage = await dbContext.OutboxMessages
                    .FromSqlInterpolated($"SELECT * FROM \"OutboxMessages\" WHERE \"Id\" = {msg.Id} AND \"Status\" = {(int)OutboxStatus.Pending} FOR UPDATE SKIP LOCKED")
                    .FirstOrDefaultAsync(cancellationToken);

                if (lockedMessage == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue; // El mensaje ya fue procesado o tomado por otro hilo
                }

                _logger.LogInformation("Procesando evento Outbox {EventId} tipo {EventType}", lockedMessage.Id, lockedMessage.EventType);

                var payloadObject = JsonSerializer.Deserialize<N8nEventPayload<object>>(lockedMessage.PayloadJson);

                if (payloadObject != null)
                {
                    await workflowGateway.TriggerWorkflowAsync("nexflow-events", payloadObject, cancellationToken);
                }

                lockedMessage.Status = OutboxStatus.Processed;
                lockedMessage.ProcessedAt = DateTime.UtcNow;
                lockedMessage.Error = null;

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando mensaje Outbox {EventId}", msg.Id);
                await transaction.RollbackAsync(cancellationToken);

                // Reintento en una transacción independiente y rápida
                using var retryTx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                var retryMsg = await dbContext.OutboxMessages.FindAsync(new object[] { msg.Id }, cancellationToken);
                if (retryMsg != null)
                {
                    retryMsg.RetryCount++;
                    retryMsg.Error = ex.Message;

                    if (retryMsg.RetryCount >= 5)
                    {
                        retryMsg.Status = OutboxStatus.Failed;
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                await retryTx.CommitAsync(cancellationToken);
            }
        }
    }
}