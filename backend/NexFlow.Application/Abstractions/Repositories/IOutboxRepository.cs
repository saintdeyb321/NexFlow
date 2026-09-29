using NexFlow.Domain.Entities.System;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IOutboxRepository
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken);
    Task<OutboxMessage?> ClaimAsync(CancellationToken cancellationToken);
    Task FinishAsync(OutboxMessage message, bool success, string? error, bool terminal, CancellationToken cancellationToken);
}
