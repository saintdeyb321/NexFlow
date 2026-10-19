using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Notifications;

namespace NexFlow.Infrastructure.Workers;

public class InboundMessageWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InboundMessageWorker> _logger;
    private DateTime _nextObservation;

    public InboundMessageWorker(IServiceProvider serviceProvider, ILogger<InboundMessageWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Trace("WorkerStarted", null, null);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= _nextObservation)
                {
                    _nextObservation = DateTime.UtcNow.AddMinutes(1);
                    await ObserveQueueAsync(stoppingToken);
                }
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
                    using var correlation = Correlate(msg.WorkspaceId, msg.Id);
                    Trace("Claimed", msg.WorkspaceId, msg.Id);
                    using var messageScope = _serviceProvider.CreateScope();
                    var msgRepo = messageScope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
                    using var handlerScope = _serviceProvider.CreateScope();
                    var handler = handlerScope.ServiceProvider.GetRequiredService<ProcessIncomingMessageCommandHandler>();

                    try
                    {
                        await msgRepo.ProcessClaimedAsync(msg, async () =>
                        {
                            var command = JsonSerializer.Deserialize<ProcessIncomingMessageCommand>(msg.PayloadJson)
                                ?? throw new JsonException("Inbound payload is null.");
                            if (string.IsNullOrWhiteSpace(command.MessageText) ||
                                string.IsNullOrWhiteSpace(command.MessageId) || string.IsNullOrWhiteSpace(command.InstanceName) ||
                                command.MessageId != msg.ExternalMessageId || command.InstanceName != msg.InstanceName ||
                                command.CustomerPhone != msg.Phone ||
                                (command.WorkspaceId.HasValue && command.WorkspaceId != msg.WorkspaceId))
                                throw new JsonException("Inbound payload does not match its durable envelope.");
                            await handler.Handle(command with { WorkspaceId = msg.WorkspaceId }, stoppingToken);
                            Trace("HandlerCompletedNotDelivery", msg.WorkspaceId, msg.Id);
                        }, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var failure = FailureKind(ex);
                        Trace("HandlerFailed", msg.WorkspaceId, msg.Id, failure);
                        await msgRepo.FailMessageAsync(msg.Id, msg.ProcessingStartedAt!.Value, failure, stoppingToken);
                    }
                });

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Trace("WorkerRetry", null, null, FailureKind(ex));
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ObserveQueueAsync(CancellationToken stoppingToken)
    {
        // Separate scope/transaction and bounded observation. Failure never aborts a turn.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IInboundMessageRepository>();
            var stalled = await repo.GetStalledAsync(DateTime.UtcNow.AddMinutes(-5), 50, deadline.Token);
            foreach (var item in stalled)
            {
                try
                {
                    _logger.LogWarning("InboundStalled WorkspaceId={WorkspaceId} InboundId={InboundId} Status={Status} ReceivedAt={ReceivedAt} NextRetryAt={NextRetryAt} BlockedInboundId={BlockedInboundId}",
                        item.WorkspaceId, item.Id, item.Status, item.ReceivedAt, item.NextRetryAt, item.BlockedInboundId);
                }
                catch { /* Logging is best effort. */ }
            }
            foreach (var tenant in stalled.Where(x => x.WorkspaceId.HasValue).GroupBy(x => x.WorkspaceId!.Value))
            {
                try
                {
                    var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
                    await notifications.TryCreateWithCooldownAsync(tenant.Key, new NotificationRecord
                    {
                        ModuleCode = "CONVERSATIONS", Type = NotificationType.SystemAlert,
                        Title = "Mensajes pendientes de recuperación",
                        Message = $"Entrada {tenant.First().Id}: procesamiento demorado. Se conserva el orden y los reintentos seguros; revisa el diagnóstico.",
                        ActionUrl = "/inbox"
                    }, TimeSpan.FromHours(1), deadline.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { Trace("QueueAlertUnavailable", tenant.Key, null, FailureKind(ex)); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { Trace("QueueObservationUnavailable", null, null, FailureKind(ex)); }
    }

    private IDisposable? Correlate(Guid? workspaceId, Guid inboundId)
    {
        try { return _logger.BeginScope(new Dictionary<string, object?> { ["WorkspaceId"] = workspaceId, ["InboundId"] = inboundId }); }
        catch { return null; }
    }

    private void Trace(string stage, Guid? workspaceId, Guid? inboundId, string? exceptionType = null)
    {
        try { _logger.Log(exceptionType == null ? LogLevel.Information : LogLevel.Error,
            "InboundStage={Stage} WorkspaceId={WorkspaceId} InboundId={InboundId} FailureKind={FailureKind}", stage, workspaceId, inboundId, exceptionType); }
        catch { /* Logging must not interfere with processing or retries. */ }
    }

    private static string FailureKind(Exception error) => error switch
    {
        Grpc.Core.RpcException rpc => $"Firestore.{rpc.StatusCode}",
        Npgsql.PostgresException postgres => $"PostgreSQL.{postgres.SqlState}",
        HttpRequestException { StatusCode: { } code } => $"HTTP.{(int)code}",
        _ => error.GetType().Name
    };
}
