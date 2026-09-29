using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.ProcessMessage;

namespace NexFlow.Infrastructure.Workers;

public class InboundMessageWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InboundMessageWorker> _logger;

    public InboundMessageWorker(IServiceProvider serviceProvider, ILogger<InboundMessageWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InboundMessageWorker (Durable Inbox) iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();

                // Descargamos y bloqueamos lote de mensajes seguros
                var messages = await repo.GetAndLockNextMessagesAsync(10, stoppingToken);

                if (!messages.Any())
                {
                    await Task.Delay(1000, stoppingToken); // Descanso si no hay tráfico
                    continue;
                }

                // 🔥 Procesamiento paralelo aislado por cada mensaje
                var tasks = messages.Select(async msg =>
                {
                    using var messageScope = _serviceProvider.CreateScope();
                    var msgRepo = messageScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
                    var handler = messageScope.ServiceProvider.GetRequiredService<ProcessIncomingMessageCommandHandler>();

                    try
                    {
                        var command = JsonSerializer.Deserialize<ProcessIncomingMessageCommand>(msg.PayloadJson);
                        if (command != null)
                        {
                            await handler.Handle(command, stoppingToken);
                        }
                        await msgRepo.CompleteMessageAsync(msg.Id, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error procesando mensaje Inbound {Id}", msg.Id);
                        await msgRepo.FailMessageAsync(msg.Id, ex.Message, stoppingToken);
                    }
                });

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo en InboundMessageWorker. Reintentando en 5s...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}