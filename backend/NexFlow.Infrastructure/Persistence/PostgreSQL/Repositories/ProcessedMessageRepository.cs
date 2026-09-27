using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class ProcessedMessageRepository : IProcessedMessageRepository
{
    private readonly NexFlowDbContext _context;

    // 🔥 SPRINT 15: Timeout de recuperación.
    private readonly TimeSpan _processingTimeout = TimeSpan.FromMinutes(2);

    public ProcessedMessageRepository(NexFlowDbContext context)
    {
        _context = context;
    }

    public async Task<bool> BeginProcessingAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expiredBefore = now - _processingTimeout;
        var claimed = await _context.ProcessedMessages
            .Where(m => m.WorkspaceId == workspaceId && m.MessageId == messageId
                && (m.Status == "PENDING" || m.Status == "FAILED"
                    || (m.Status == "PROCESSING" && m.ProcessingStartedAt < expiredBefore)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, "PROCESSING")
                .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                .SetProperty(m => m.ProcessingStartedAt, now), cancellationToken);
        if (claimed == 1) return true;

        if (await _context.ProcessedMessages.AnyAsync(
            m => m.WorkspaceId == workspaceId && m.MessageId == messageId, cancellationToken))
            return false;

        var newRecord = new ProcessedMessage
        {
            WorkspaceId = workspaceId,
            MessageId = messageId,
            Status = "PROCESSING",
            Attempts = 1,
            ProcessingStartedAt = now,
            ReceivedAt = now
        };
        _context.ProcessedMessages.Add(newRecord);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
            { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            _context.Entry(newRecord).State = EntityState.Detached;
            return false;
        }
    }
    public async Task MarkAsProcessedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var record = await _context.ProcessedMessages
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.MessageId == messageId, cancellationToken);

        if (record != null)
        {
            record.Status = "PROCESSED";
            record.ProcessedAt = DateTime.UtcNow;
            record.LastError = null;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAsFailedAsync(Guid workspaceId, string messageId, string error, CancellationToken cancellationToken)
    {
        var record = await _context.ProcessedMessages
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.MessageId == messageId, cancellationToken);

        if (record != null)
        {
            record.Status = "FAILED";
            record.LastError = error;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task CleanupOldMessagesAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var oldMessages = await _context.ProcessedMessages
            .Where(m => m.ReceivedAt < cutoff)
            .ToListAsync(cancellationToken);

        _context.ProcessedMessages.RemoveRange(oldMessages);
        await _context.SaveChangesAsync(cancellationToken);
    }
}