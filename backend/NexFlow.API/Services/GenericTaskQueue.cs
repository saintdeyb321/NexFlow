using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace NexFlow.API.Services.BackgroundServices;

// 1. La Interfaz Genérica (La que inyectamos en CatalogController)
public interface IBackgroundTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem);
    ValueTask<Func<IServiceProvider, CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken);
}

// 2. La Implementación de la Cola (En memoria)
public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, ValueTask>> _queue;

    public BackgroundTaskQueue(int capacity = 500)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, ValueTask>>(options);
    }

    public async ValueTask QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem)
    {
        if (workItem == null) throw new ArgumentNullException(nameof(workItem));
        await _queue.Writer.WriteAsync(workItem);
    }

    public async ValueTask<Func<IServiceProvider, CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}

// 3. El Worker que procesa la cola genérica en segundo plano
public class GenericBackgroundWorker : BackgroundService
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GenericBackgroundWorker> _logger;

    public GenericBackgroundWorker(
        IBackgroundTaskQueue taskQueue,
        IServiceProvider serviceProvider,
        ILogger<GenericBackgroundWorker> logger)
    {
        _taskQueue = taskQueue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Generic Background Worker iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _taskQueue.DequeueAsync(stoppingToken);

                // Disparamos la tarea de forma concurrente sin bloquear la cola
                _ = ProcessWorkItemAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Ignorar durante el apagado del host
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo crítico extrayendo tarea de la cola genérica.");
            }
        }
    }

    private async Task ProcessWorkItemAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem, CancellationToken stoppingToken)
    {
        try
        {
            await workItem(_serviceProvider, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando una tarea genérica en segundo plano.");
        }
    }
}