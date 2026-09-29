using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class InboundMessageRepository : IInboundMessageRepository
{
    private readonly NexFlowDbContext _context;

    public InboundMessageRepository(NexFlowDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        _context.Set<InboundMessage>().Add(message);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<InboundMessage>> GetAndLockNextMessagesAsync(int limit, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // 🔥 SPRINT 01: PostgreSQL FOR UPDATE SKIP LOCKED
        // Bloquea los registros a nivel de base de datos mientras los procesamos
        var sql = @"
            SELECT * FROM ""InboundMessages""
            WHERE ""Status"" = 0 
               OR (""Status"" = 1 AND ""NextRetryAt"" <= {0}) 
               OR (""Status"" = 3 AND ""NextRetryAt"" <= {0})
            ORDER BY ""ReceivedAt"" ASC
            LIMIT {1}
            FOR UPDATE SKIP LOCKED";

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

        return messages;
    }

    public async Task CompleteMessageAsync(Guid id, CancellationToken cancellationToken)
    {
        var msg = await _context.Set<InboundMessage>().FindAsync(new object[] { id }, cancellationToken);
        if (msg != null)
        {
            msg.Status = InboundMessageStatus.Processed;
            msg.ProcessedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task FailMessageAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        var msg = await _context.Set<InboundMessage>().FindAsync(new object[] { id }, cancellationToken);
        if (msg != null)
        {
            msg.Attempts++;
            msg.LastError = error;

            if (msg.Attempts >= 5)
            {
                msg.Status = InboundMessageStatus.DeadLetter;
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