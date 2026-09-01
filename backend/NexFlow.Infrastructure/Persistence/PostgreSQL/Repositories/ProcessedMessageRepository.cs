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

    public async Task<bool> TryAcquireLockAsync(Guid workspaceId, string messageId, CancellationToken cancellationToken)
    {
        var record = new ProcessedMessage
        {
            WorkspaceId = workspaceId,
            MessageId = messageId,
            ProcessedAt = DateTime.UtcNow
        };

        _dbContext.ProcessedMessages.Add(record);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true; // Éxito: el mensaje es nuevo
        }
        catch (DbUpdateException)
        {
            // Fallo: llave duplicada (Evolution envió el mismo webhook dos veces)
            return false;
        }
    }

    public async Task CleanupOldMessagesAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.AddDays(-retentionDays);
        var deletedCount = await _dbContext.ProcessedMessages
            .Where(p => p.ProcessedAt < threshold)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Limpieza de Idempotencia completada: {Count} mensajes antiguos eliminados.", deletedCount);
    }
}