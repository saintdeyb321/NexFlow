namespace NexFlow.Application.Abstractions.Repositories;

public interface IProcessedMessageRepository
{
    Task<bool> TryAcquireLockAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken);
    Task CleanupOldMessagesAsync(int retentionDays, CancellationToken cancellationToken);
}