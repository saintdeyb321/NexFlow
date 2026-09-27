using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities.System;
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
        var workflowGateway = scope.ServiceProvider.GetRequiredService<IWorkflowGateway>();
        var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        var pendingMessages = await outboxRepo.GetPendingMessagesAsync(20, cancellationToken);

        foreach (var message in pendingMessages)
        {
            await using var lockConnection = new Npgsql.NpgsqlConnection(config.GetConnectionString("Postgres"));
            await lockConnection.OpenAsync(cancellationToken);

            var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(message.Id));
            var lockKey = BitConverter.ToInt64(hashBytes, 0);

            await using var claimCommand = new Npgsql.NpgsqlCommand("SELECT pg_try_advisory_lock(@key);", lockConnection);
            claimCommand.Parameters.AddWithValue("key", lockKey);

            var claimed = (bool?)await claimCommand.ExecuteScalarAsync(cancellationToken) == true;

            if (!claimed)
            {
                continue;
            }

            try
            {
                _logger.LogInformation("Procesando evento Outbox {EventId} tipo {EventType}", message.Id, message.EventType);

                var payloadObject = JsonSerializer.Deserialize<N8nEventPayload<object>>(message.PayloadJson);

                if (payloadObject != null)
                {
                    await workflowGateway.TriggerWorkflowAsync("nexflow-events", payloadObject, cancellationToken);
                }

                message.Status = OutboxStatus.Processed;
                message.ProcessedAt = DateTime.UtcNow;
                message.Error = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando mensaje Outbox {EventId}", message.Id);
                message.RetryCount++;
                message.Error = ex.Message;

                if (message.RetryCount >= 5)
                {
                    message.Status = OutboxStatus.Failed;
                }
            }
            finally
            {
                // Aseguramos la persistencia del estado en BD antes de liberar el lock
                await outboxRepo.UpdateAsync(message, cancellationToken);

                await using var releaseCommand = new Npgsql.NpgsqlCommand("SELECT pg_advisory_unlock(@key);", lockConnection);
                releaseCommand.Parameters.AddWithValue("key", lockKey);
                await releaseCommand.ExecuteScalarAsync(cancellationToken);
            }
        }
    }
}