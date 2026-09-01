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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var command = await _taskQueue.DequeueAsync(stoppingToken);

                // Se procesa en su propio scope, totalmente desacoplado de la petición HTTP
                using var scope = _serviceProvider.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ProcessIncomingMessageCommandHandler>();

                await handler.Handle(command, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Ignorar si el host se está apagando
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo en el procesamiento de fondo del Webhook de Evolution.");
            }
        }
    }
}