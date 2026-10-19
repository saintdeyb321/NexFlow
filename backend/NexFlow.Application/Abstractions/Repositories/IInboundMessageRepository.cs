using NexFlow.Domain.Entities.System;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IInboundMessageRepository
{
    Task AddAsync(InboundMessage message, CancellationToken cancellationToken);
    Task<IEnumerable<InboundMessage>> GetAndLockNextMessagesAsync(int limit, CancellationToken cancellationToken);
    Task ProcessClaimedAsync(InboundMessage claim, Func<Task> process, CancellationToken cancellationToken);
    Task CompleteMessageAsync(Guid id, DateTime processingStartedAt, CancellationToken cancellationToken);
    Task FailMessageAsync(Guid id, DateTime processingStartedAt, string error, CancellationToken cancellationToken);
    Task<IReadOnlyList<InboundQueueDiagnostic>> GetStalledAsync(DateTime receivedBefore, int limit, CancellationToken cancellationToken);
}

// Diagnostic projection only: never expose payloads, phone numbers or provider identifiers.
public sealed class InboundQueueDiagnostic
{
    public Guid Id { get; set; }
    public Guid? WorkspaceId { get; set; }
    public InboundMessageStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public Guid? BlockedInboundId { get; set; }
}
