using NexFlow.Domain.Entities.System;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IInboundMessageRepository
{
    Task AddAsync(InboundMessage message, CancellationToken cancellationToken);
    Task<IEnumerable<InboundMessage>> GetAndLockNextMessagesAsync(int limit, CancellationToken cancellationToken);
    Task ProcessClaimedAsync(InboundMessage claim, Func<Task> process, CancellationToken cancellationToken);
    Task CompleteMessageAsync(Guid id, DateTime processingStartedAt, CancellationToken cancellationToken);
    Task FailMessageAsync(Guid id, DateTime processingStartedAt, string error, CancellationToken cancellationToken);
}
