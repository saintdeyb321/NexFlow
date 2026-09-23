namespace NexFlow.Application.Abstractions.Repositories;

public interface IProcessedMessageRepository
{
    // 🔥 SPRINT 1: Máquina de estados transaccional
    Task<bool> BeginProcessingAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
    Task MarkAsProcessedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
    Task MarkAsFailedAsync(Guid workspaceId, string messageId, string error, CancellationToken cancellationToken);
    Task CleanupOldMessagesAsync(int retentionDays, CancellationToken cancellationToken);
}