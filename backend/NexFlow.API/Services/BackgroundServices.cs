using System.Collections.Concurrent;
using System.Threading.Channels;
using NexFlow.Application.Features.Automation.ProcessMessage;

namespace NexFlow.API.Services.BackgroundServices;

public interface IWebhookTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(ProcessIncomingMessageCommand command);
    ValueTask<ProcessIncomingMessageCommand> DequeueAsync(CancellationToken cancellationToken);
}

public class WebhookTaskQueue : IWebhookTaskQueue
{
    private readonly Channel<ProcessIncomingMessageCommand> _queue;

    public WebhookTaskQueue(int capacity = 1000)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<ProcessIncomingMessageCommand>(options);
    }

    public async ValueTask QueueBackgroundWorkItemAsync(ProcessIncomingMessageCommand command)
    {
        await _queue.Writer.WriteAsync(command);
    }

    public async ValueTask<ProcessIncomingMessageCommand> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}

public class WebhookProcessingBackgroundService : BackgroundService
{
    private readonly IWebhookTaskQueue _taskQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookProcessingBackgroundService> _logger;

    // Franjas estables: no se elimina un bloqueo mientras otro consumidor puede usarlo.
    private static readonly SemaphoreSlim[] ConversationLocks = Enumerable.Range(0, 1024)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public WebhookProcessingBackgroundService(
        IWebhookTaskQueue taskQueue,
        IServiceProvider serviceProvider,
        ILogger<WebhookProcessingBackgroundService> logger)
    {
        _taskQueue = taskQueue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => DispatchAsync(stoppingToken)));
    }

    private async Task DispatchAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var command = await _taskQueue.DequeueAsync(stoppingToken);

                await ProcessMessageConcurrentAsync(command, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo crítico en el despachador de la cola del Webhook.");
            }
        }
    }

    private async Task ProcessMessageConcurrentAsync(ProcessIncomingMessageCommand command, CancellationToken stoppingToken)
    {
        // Llave única para particionar la concurrencia: Instancia + Teléfono.
        string lockKey = $"{command.InstanceName}_{command.CustomerPhone}";

        // Seleccionamos un cerrojo estable para este chat.
        var semaphore = ConversationLocks[(int)((uint)StringComparer.Ordinal.GetHashCode(lockKey) % (uint)ConversationLocks.Length)];

        // Esperamos turno si ya hay otro mensaje de ESTE MISMO chat procesándose.
        // Los chats diferentes no esperarán y se procesarán en paralelo.
        await semaphore.WaitAsync(stoppingToken);
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ProcessIncomingMessageCommandHandler>();

            await handler.Handle(command, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error aisaldo procesando mensaje {MessageId} de {Phone}.", command.MessageId, command.CustomerPhone);
        }
        finally
        {
            semaphore.Release();

        }
    }
}
