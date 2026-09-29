using Microsoft.EntityFrameworkCore;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

public static class TenantLifecycleLock
{
    // A separate transaction owns the lock; business transactions remain independent.
    public static Task AcquireAsync(NexFlowDbContext db, Guid workspaceId, bool exclusive, CancellationToken ct) => exclusive
        ? db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({workspaceId.ToString()}, 0))", ct)
        : db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared(hashtextextended({workspaceId.ToString()}, 0))", ct);
}
