using Microsoft.EntityFrameworkCore;
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

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Reducido a 5s para mayor agilidad
        }
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
        var workflowGateway = scope.ServiceProvider.GetRequiredService<IWorkflowGateway>();

        // 🔥 SPRINT 11: 1. RECLAMO DE EVENTOS (Transacción Ultra-Corta SQL)
        var messagesToProcess = new List<OutboxMessage>();

        using (var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken))
        {
            // Seleccionamos los próximos 20 y los marcamos como PROCESSING instantáneamente usando FOR UPDATE SKIP LOCKED
            var claimedMessages = await dbContext.OutboxMessages
                .FromSqlInterpolated($"SELECT * FROM \"OutboxMessages\" WHERE \"Status\" = {(int)OutboxStatus.Pending} ORDER BY \"CreatedAt\" LIMIT 20 FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);

            if (!claimedMessages.Any())
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            foreach (var msg in claimedMessages)
            {
                msg.Status = OutboxStatus.Processing;
                messagesToProcess.Add(msg);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // 🔥 SPRINT 11: 2. EJECUCIÓN HTTP (Totalmente fuera de la transacción SQL)
        foreach (var msg in messagesToProcess)
        {
            try
            {
                _logger.LogInformation("Enviando evento Outbox {EventId} a n8n", msg.Id);
                var payloadObject = JsonSerializer.Deserialize<N8nEventPayload<object>>(msg.PayloadJson);

                if (payloadObject != null)
                {
                    // La DB no sufre si n8n tarda 10 segundos
                    await workflowGateway.TriggerWorkflowAsync("nexflow-events", payloadObject, cancellationToken);
                }

                // 3. ACTUALIZACIÓN POST-HTTP (Nueva Transacción Corta)
                using var finalTx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                var messageToUpdate = await dbContext.OutboxMessages.FindAsync(new object[] { msg.Id }, cancellationToken);
                if (messageToUpdate != null)
                {
                    messageToUpdate.Status = OutboxStatus.Processed;
                    messageToUpdate.ProcessedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                await finalTx.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falla HTTP al enviar mensaje Outbox {EventId}", msg.Id);

                using var retryTx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                var retryMsg = await dbContext.OutboxMessages.FindAsync(new object[] { msg.Id }, cancellationToken);
                if (retryMsg != null)
                {
                    retryMsg.RetryCount++;
                    retryMsg.Error = ex.Message;
                    retryMsg.Status = retryMsg.RetryCount >= 5 ? OutboxStatus.Failed : OutboxStatus.Pending; // Lo regresa a Pending para reintento
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                await retryTx.CommitAsync(cancellationToken);
            }
        }
    }
}