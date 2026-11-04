using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

// Also protects tracked writes bypassing IReservationEngine (e.g. web handlers).
public sealed class DemoReservationWriteInterceptor(IAgentDemoPolicy policy) : SaveChangesInterceptor
{
    private void Guard(DbContext? context)
    {
        if (context == null) return;
        foreach (var entry in context.ChangeTracker.Entries<Reservation>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            policy.DemandCommercialWrites(entry.Entity.WorkspaceId);
    }
    public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result) { Guard(data.Context); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); Guard(data.Context); return ValueTask.FromResult(result); }
}
