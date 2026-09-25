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
        var existing = await _context.ProcessedMessages
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.MessageId == messageId, cancellationToken);

        if (existing != null)
        {
            if (existing.Status == "PROCESSED") return false; // Ya se procesó

            if (existing.Status == "PROCESSING")
            {
                // 🔥 SPRINT 15: Verificamos si es un mensaje huérfano (Worker muerto)
                if ((DateTime.UtcNow - existing.ProcessingStartedAt) > _processingTimeout)
                {
                    existing.Attempts++;
                    existing.ProcessingStartedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                    return true;
                }
                return false;
            }

            if (existing.Status == "FAILED")
            {
                existing.Status = "PROCESSING";
                existing.Attempts++;
                existing.ProcessingStartedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }

            return false;
        }

        // Mensaje nuevo (Sin propiedad Id, usando ReceivedAt)
        var newRecord = new ProcessedMessage
        {
            WorkspaceId = workspaceId,
            MessageId = messageId,
            Status = "PROCESSING",
            Attempts = 1,
            ProcessingStartedAt = DateTime.UtcNow,
            ReceivedAt = DateTime.UtcNow
        };

        _context.ProcessedMessages.Add(newRecord);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
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