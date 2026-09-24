using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities.System;

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

        var pendingMessages = await outboxRepo.GetPendingMessagesAsync(20, cancellationToken);

        foreach (var message in pendingMessages)
        {
            try
            {
                _logger.LogInformation("Procesando evento Outbox {EventId} tipo {EventType}", message.Id, message.EventType);

                // Reconstruimos el payload genérico dinámicamente o lo enviamos como object/string
                // Nota: Tu IWorkflowGateway actual requiere un tipo genérico <T>. 
                // Usaremos <object> ya que el JSON ya está serializado y lo pasaremos de forma transparente.
                var payloadObject = JsonSerializer.Deserialize<N8nEventPayload<object>>(message.PayloadJson);

                if (payloadObject != null)
                {
                    await workflowGateway.TriggerWorkflowAsync("nexflow-events", payloadObject, cancellationToken);
                }

                message.Status = OutboxStatus.Processed;
                message.ProcessedAt = DateTime.UtcNow;
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

            await outboxRepo.UpdateAsync(message, cancellationToken);
        }
    }
}