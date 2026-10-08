using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public sealed class WhatsAppConnectionRepository(NexFlowDbContext db) : IWhatsAppConnectionRepository
{
    public async Task<WhatsAppConnectionSnapshot> GetAsync(Guid workspaceId, CancellationToken ct)
    {
        if (workspaceId == Guid.Empty) throw new UnauthorizedAccessException("Workspace requerido.");
        var workspace = await db.Workspaces.AsNoTracking().SingleOrDefaultAsync(w => w.Id == workspaceId, ct)
            ?? throw new KeyNotFoundException("Workspace no encontrado.");
        if (workspace.Status == WorkspaceStatus.Deleting) throw new ConcurrencyException("Workspace en eliminación.");
        if (string.IsNullOrWhiteSpace(workspace.EvolutionInstanceName)) throw new ConcurrencyException("La instancia del workspace requiere aprovisionamiento.");
        var normalized = EvolutionInstanceIdentity.LegacyAlias(workspace.EvolutionInstanceName);
        if (await db.Workspaces.AnyAsync(w => w.Id != workspaceId && w.EvolutionInstanceName != null
            && w.EvolutionInstanceName.Replace("-", "").Replace(" ", "").ToLower() == normalized, ct))
            throw new ConcurrencyException("El nombre de instancia antiguo es ambiguo y requiere revisión administrativa.");
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ""WhatsAppConnections"" (""WorkspaceId"", ""PersistenceVersion"", ""Status"", ""LogoutPending"")
            VALUES ({workspaceId}, {Guid.NewGuid()}, 'DISCONNECTED', FALSE) ON CONFLICT (""WorkspaceId"") DO NOTHING", ct);
        return new(workspace.EvolutionInstanceName, await db.WhatsAppConnections.AsNoTracking().SingleAsync(c => c.WorkspaceId == workspaceId, ct));
    }

    public async Task<Guid> AcquireAsync(Guid workspaceId, bool logout, DateTime now, DateTime until, CancellationToken ct)
    {
        await using var lifecycle = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql()) await TenantLifecycleLock.AcquireAsync(db, workspaceId, false, ct);
        await GetAsync(workspaceId, ct);
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId && w.Status == WorkspaceStatus.Active, ct)) throw new UnauthorizedAccessException("Workspace no disponible.");
        var id = Guid.NewGuid();
        var changed = await db.WhatsAppConnections.Where(c => c.WorkspaceId == workspaceId
            && (c.OperationUntil == null || c.OperationUntil <= now) && (logout || !c.LogoutPending))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.OperationId, id).SetProperty(c => c.OperationUntil, until)
                .SetProperty(c => c.LogoutPending, c => logout || c.LogoutPending).SetProperty(c => c.PersistenceVersion, Guid.NewGuid()), ct);
        if (changed != 1) throw new ConcurrencyException("Hay una operación de WhatsApp pendiente. Consulta el estado antes de reintentar.");
        await lifecycle.CommitAsync(ct);
        return id;
    }

    public async Task SaveObservationAsync(Guid workspaceId, Guid? operationId, WhatsAppObservation observation, DateTime now, CancellationToken ct, Guid? expectedVersion = null)
    {
        var query = db.WhatsAppConnections.Where(c => c.WorkspaceId == workspaceId && !c.LogoutPending);
        if (operationId.HasValue) query = query.Where(c => c.OperationId == operationId && c.OperationUntil > now);
        if (expectedVersion.HasValue) query = query.Where(c => c.PersistenceVersion == expectedVersion);
        var changed = await query.ExecuteUpdateAsync(s =>
            s.SetProperty(c => c.IsLinked, c => observation.HasSession || c.IsLinked == true)
             .SetProperty(c => c.Status, c => observation.HasSession && observation.Status == "CONNECTED" ? "CONNECTED"
                 : c.IsLinked == true && observation.Status != "CONNECTED" ? "RECONNECTING"
                 : operationId == null && c.QrBase64 != null && c.QrExpiresAt > now ? "QR_AVAILABLE" : observation.Status)
             .SetProperty(c => c.QrBase64, c => observation.HasSession || c.IsLinked == true ? null : operationId == null ? c.QrBase64 : observation.QrBase64)
             .SetProperty(c => c.QrExpiresAt, c => observation.HasSession || c.IsLinked == true ? null : operationId == null ? c.QrExpiresAt : observation.QrExpiresAt)
             .SetProperty(c => c.ObservedAt, now).SetProperty(c => c.PersistenceVersion, Guid.NewGuid()), ct);
        if (operationId.HasValue && changed != 1) throw new ConcurrencyException("La operación de WhatsApp ya no está vigente.");
    }

    public async Task ConfirmLogoutAsync(Guid workspaceId, Guid operationId, string? owner, DateTime now, CancellationToken ct)
    {
        var changed = await db.WhatsAppConnections.Where(c => c.WorkspaceId == workspaceId && c.OperationId == operationId && c.OperationUntil > now && c.LogoutPending)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsLinked, false).SetProperty(c => c.Status, "DISCONNECTED")
                .SetProperty(c => c.QrBase64, (string?)null).SetProperty(c => c.QrExpiresAt, (DateTime?)null)
                .SetProperty(c => c.LogoutPending, false).SetProperty(c => c.LastLogoutAt, now).SetProperty(c => c.ObservedAt, now)
                .SetProperty(c => c.LoggedOutOwner, owner).SetProperty(c => c.PersistenceVersion, Guid.NewGuid()), ct);
        if (changed != 1) throw new ConcurrencyException("No se pudo confirmar la desconexión. Reintenta explícitamente.");
    }

    public Task ReleaseAsync(Guid workspaceId, Guid operationId, CancellationToken ct) => db.WhatsAppConnections
        .Where(c => c.WorkspaceId == workspaceId && c.OperationId == operationId)
        .ExecuteUpdateAsync(s => s.SetProperty(c => c.OperationId, (Guid?)null).SetProperty(c => c.OperationUntil, (DateTime?)null)
            .SetProperty(c => c.PersistenceVersion, Guid.NewGuid()), ct);

    public async Task AdoptLegacyNameAsync(Guid workspaceId, string oldName, string providerName, CancellationToken ct)
    {
        // Do not transfer an alias if either an exact name or another legacy name claims it.
        var owners = await db.Workspaces.AsNoTracking().Where(w => w.EvolutionInstanceName != null)
            .Select(w => new { w.Id, w.EvolutionInstanceName }).ToListAsync(ct);
        if (owners.Any(w => w.Id != workspaceId && (w.EvolutionInstanceName == providerName || EvolutionInstanceIdentity.LegacyAlias(w.EvolutionInstanceName!) == providerName)))
            throw new ConcurrencyException("El nombre antiguo es ambiguo; la vinculación requiere revisión administrativa.");
        var changed = await db.Workspaces.Where(w => w.Id == workspaceId && w.EvolutionInstanceName == oldName)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, providerName), ct);
        if (changed != 1) throw new ConcurrencyException("La identidad de instancia cambió.");
    }
}
