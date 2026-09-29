using NexFlow.Domain.Entities.System;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IInboundMessageRepository
{
    Task AddAsync(InboundMessage message, CancellationToken cancellationToken);
    Task<IEnumerable<InboundMessage>> GetAndLockNextMessagesAsync(int limit, CancellationToken cancellationToken);
    Task CompleteMessageAsync(Guid id, CancellationToken cancellationToken);
    Task FailMessageAsync(Guid id, string error, CancellationToken cancellationToken);
}