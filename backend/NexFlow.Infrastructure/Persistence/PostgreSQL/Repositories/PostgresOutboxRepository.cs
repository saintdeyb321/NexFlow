using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class PostgresOutboxRepository(NexFlowDbContext context) : IOutboxRepository
{
    public async Task AddAsync(OutboxMessage message, CancellationToken ct) => await context.OutboxMessages.AddAsync(message, ct);

    public async Task<OutboxMessage?> ClaimAsync(CancellationToken ct)
    {
        var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        var messages = await context.OutboxMessages.FromSqlInterpolated($"SELECT * FROM \"OutboxMessages\" WHERE (\"Status\" = {(int)OutboxStatus.Pending} AND (\"NextRetryAt\" IS NULL OR \"NextRetryAt\" <= {now})) OR (\"Status\" = {(int)OutboxStatus.Processing} AND (\"LeaseUntil\" IS NULL OR \"LeaseUntil\" <= {now})) ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
        var message = messages.FirstOrDefault();
        if (message != null)
        {
            message.Status = OutboxStatus.Processing;
            message.ProcessingStartedAt = now;
            message.LeaseUntil = now.AddMinutes(2);
            message.RetryCount++;
            await context.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (message != null) context.Entry(message).State = EntityState.Detached;
        return message;
    }

    public Task FinishAsync(OutboxMessage message, bool success, string? error, bool terminal, CancellationToken ct)
    {
        var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        var status = success ? OutboxStatus.Processed : terminal || message.RetryCount >= 5 ? OutboxStatus.Failed : OutboxStatus.Pending;
        var retryAt = status == OutboxStatus.Pending ? now.AddSeconds(Math.Min(300, 5 * Math.Pow(2, message.RetryCount - 1))) : (DateTime?)null;
        return context.OutboxMessages.Where(m => m.Id == message.Id && m.WorkspaceId == message.WorkspaceId && m.Status == OutboxStatus.Processing && m.ProcessingStartedAt == message.ProcessingStartedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, status).SetProperty(m => m.ProcessedAt, success ? now : (DateTime?)null)
                .SetProperty(m => m.NextRetryAt, retryAt).SetProperty(m => m.LeaseUntil, (DateTime?)null).SetProperty(m => m.Error, error), ct);
    }
}
