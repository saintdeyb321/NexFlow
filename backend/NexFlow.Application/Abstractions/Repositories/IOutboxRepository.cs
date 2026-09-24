using NexFlow.Domain.Entities.System;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IOutboxRepository
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken);
    Task<IEnumerable<OutboxMessage>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken);
    Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken);
}