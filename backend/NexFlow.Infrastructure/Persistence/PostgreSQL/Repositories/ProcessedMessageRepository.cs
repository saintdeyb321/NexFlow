using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class ProcessedMessageRepository : IProcessedMessageRepository
{
    private readonly NexFlowDbContext _dbContext;
    private readonly ILogger<ProcessedMessageRepository> _logger;

    public ProcessedMessageRepository(NexFlowDbContext dbContext, ILogger<ProcessedMessageRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> BeginProcessingAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var existingRecord = await _dbContext.ProcessedMessages
            .FirstOrDefaultAsync(p => p.WorkspaceId == workspaceId && p.MessageId == messageId, cancellationToken);

        if (existingRecord != null)
        {
            // Si ya se procesó o se está procesando ahora mismo, rechazamos el duplicado
            if (existingRecord.Status == "PROCESSED" || existingRecord.Status == "PROCESSING")
                return false;

            // Si falló anteriormente, permitimos el reintento
            existingRecord.Status = "PROCESSING";
            existingRecord.Attempts += 1;
            existingRecord.LastError = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var record = new ProcessedMessage
        {
            WorkspaceId = workspaceId,
            MessageId = messageId,
            Status = "PROCESSING",
            ReceivedAt = DateTime.UtcNow,
            Attempts = 1
        };

        _dbContext.ProcessedMessages.Add(record);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Colisión por concurrencia exacta (dos webhooks idénticos al mismo milisegundo)
            return false;
        }
    }

    public async Task MarkAsProcessedAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var record = await _dbContext.ProcessedMessages
            .FirstOrDefaultAsync(p => p.WorkspaceId == workspaceId && p.MessageId == messageId, cancellationToken);

        if (record != null)
        {
            record.Status = "PROCESSED";
            record.ProcessedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAsFailedAsync(Guid workspaceId, string messageId, string error, CancellationToken cancellationToken)
    {
        var record = await _dbContext.ProcessedMessages
            .FirstOrDefaultAsync(p => p.WorkspaceId == workspaceId && p.MessageId == messageId, cancellationToken);

        if (record != null)
        {
            record.Status = "FAILED";
            record.LastError = error;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task CleanupOldMessagesAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.AddDays(-retentionDays);
        var deletedCount = await _dbContext.ProcessedMessages
            .Where(p => p.ReceivedAt < threshold)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Limpieza de Idempotencia completada: {Count} mensajes antiguos eliminados.", deletedCount);
    }
}