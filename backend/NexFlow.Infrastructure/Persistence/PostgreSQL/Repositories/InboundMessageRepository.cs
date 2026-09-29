using NexFlow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class InboundMessageRepository : IInboundMessageRepository
{
    private readonly NexFlowDbContext _context;

    private readonly IInstanceResolver _instanceResolver;
    public InboundMessageRepository(NexFlowDbContext context, IInstanceResolver instanceResolver)
    {
        _context = context;
        _instanceResolver = instanceResolver;
    }

    public async Task AddAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        if (message.WorkspaceId is null || message.WorkspaceId == Guid.Empty)
            throw new ArgumentException("An inbound message requires a workspace.", nameof(message));

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await TenantLifecycleLock.AcquireAsync(_context, message.WorkspaceId.Value, false, cancellationToken);
        var workspace = await _context.Workspaces.AsNoTracking().SingleOrDefaultAsync(w => w.Id == message.WorkspaceId.Value, cancellationToken)
            ?? throw new KeyNotFoundException("Workspace not found.");
        if (workspace.Status == NexFlow.Domain.Enums.WorkspaceStatus.Deleting)
            throw new NexFlow.Domain.Exceptions.ConcurrencyException("Workspace is being deleted.");
        var inserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""InboundMessages""
                (""Id"", ""WorkspaceId"", ""ExternalMessageId"", ""InstanceName"", ""Phone"", ""PayloadJson"", ""Status"", ""Attempts"", ""ReceivedAt"")
            VALUES ({message.Id}, {message.WorkspaceId}, {message.ExternalMessageId}, {message.InstanceName},
                {message.Phone}, {message.PayloadJson}, {(int)message.Status}, {message.Attempts}, clock_timestamp())
            ON CONFLICT (""InstanceName"", ""ExternalMessageId"") DO NOTHING", cancellationToken);
        if (inserted == 0)
        {
            var existing = await _context.InboundMessages.AsNoTracking().SingleAsync(
                x => x.InstanceName == message.InstanceName && x.ExternalMessageId == message.ExternalMessageId, cancellationToken);
            if (existing.WorkspaceId.HasValue && existing.WorkspaceId != message.WorkspaceId)
                throw new InvalidOperationException("Inbound identity belongs to another workspace.");
        }
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task<IEnumerable<InboundMessage>> GetAndLockNextMessagesAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);

        // Only the oldest unfinished message of a tenant/phone can be claimed.
        // Failed messages retain their place while waiting for their retry.
        var sql = @"
            SELECT m.* FROM ""InboundMessages"" m
            WHERE (m.""Status"" = 0
               OR (m.""Status"" IN (1, 3) AND m.""NextRetryAt"" <= {0}))
              AND NOT EXISTS (
                SELECT 1 FROM ""InboundMessages"" predecessor
                WHERE predecessor.""Status"" IN (0, 1, 3)
                  AND ((m.""WorkspaceId"" IS NOT NULL AND predecessor.""WorkspaceId"" = m.""WorkspaceId"")
                    OR (predecessor.""InstanceName"" = m.""InstanceName""))
                  AND regexp_replace(predecessor.""Phone"", '[^0-9]', '', 'g') = regexp_replace(m.""Phone"", '[^0-9]', '', 'g')
                  AND (predecessor.""ReceivedAt"", predecessor.""Id"") < (m.""ReceivedAt"", m.""Id""))
            ORDER BY m.""ReceivedAt"", m.""Id""
            LIMIT {1}
            FOR UPDATE OF m SKIP LOCKED";
        var messages = await _context.Set<InboundMessage>()
            .FromSqlRaw(sql, now, limit)
            .ToListAsync(cancellationToken);

        foreach (var msg in messages)
        {
            msg.Status = InboundMessageStatus.Processing;
            msg.ProcessingStartedAt = now;
            // Si el servidor muere en medio del proceso, otro worker lo retomará en 5 minutos
            msg.NextRetryAt = now.AddMinutes(5);
        }

        if (messages.Any())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    public async Task ProcessClaimedAsync(InboundMessage claim, Func<Task> process, CancellationToken cancellationToken)
    {
        // This dedicated context holds the row lock for the entire external turn.
        // A stale-lease claimant uses SKIP LOCKED and cannot take a running turn.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!claim.WorkspaceId.HasValue) throw new InvalidOperationException("Inbound workspace is missing.");
            await TenantLifecycleLock.AcquireAsync(_context, claim.WorkspaceId.Value, false, cancellationToken);
            if (!await _context.Workspaces.AnyAsync(w => w.Id == claim.WorkspaceId.Value && w.Status != NexFlow.Domain.Enums.WorkspaceStatus.Deleting, cancellationToken))
                return;
            var rows = await _context.InboundMessages.FromSqlInterpolated(
                $@"SELECT * FROM ""InboundMessages"" WHERE ""Id"" = {claim.Id} FOR UPDATE")
                .ToListAsync(cancellationToken);
            var current = rows.SingleOrDefault();
            if (current == null || current.Status != InboundMessageStatus.Processing ||
                current.ProcessingStartedAt != claim.ProcessingStartedAt)
                return;

            var workspaceId = await _instanceResolver.ResolveInstanceAsync(current.InstanceName, cancellationToken);
            if (!workspaceId.HasValue || workspaceId == Guid.Empty ||
                (current.WorkspaceId.HasValue && current.WorkspaceId != workspaceId))
                throw new InvalidOperationException("Inbound instance has no matching workspace.");
            current.WorkspaceId = workspaceId;
            claim.WorkspaceId = workspaceId;
            await _context.SaveChangesAsync(cancellationToken);
            await process();
            await CompleteMessageAsync(claim.Id, claim.ProcessingStartedAt!.Value, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }
    public async Task CompleteMessageAsync(Guid id, DateTime processingStartedAt, CancellationToken cancellationToken)
    {
        var msg = await _context.Set<InboundMessage>().FindAsync(new object[] { id }, cancellationToken);
        if (msg != null && msg.Status == InboundMessageStatus.Processing && msg.ProcessingStartedAt == processingStartedAt)
        {
            msg.Status = InboundMessageStatus.Processed;
            msg.ProcessedAt = DateTime.UtcNow;
            msg.NextRetryAt = null;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task FailMessageAsync(Guid id, DateTime processingStartedAt, string error, CancellationToken cancellationToken)
    {
        var msg = await _context.Set<InboundMessage>().FindAsync(new object[] { id }, cancellationToken);
        if (msg != null && msg.Status == InboundMessageStatus.Processing && msg.ProcessingStartedAt == processingStartedAt)
        {
            msg.Attempts++;
            msg.LastError = error;

            if (msg.Attempts >= 5)
            {
                msg.Status = InboundMessageStatus.DeadLetter;
                msg.NextRetryAt = null;
            }
            else
            {
                msg.Status = InboundMessageStatus.Failed;
                // Backoff exponencial para reintentos (1m, 2m, 4m, 8m...)
                msg.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, msg.Attempts - 1));
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
