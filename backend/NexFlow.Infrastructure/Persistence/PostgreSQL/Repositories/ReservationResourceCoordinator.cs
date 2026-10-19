using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public sealed class ReservationResourceCoordinator(NexFlowDbContext db, IClock clock) : IReservationResourceCoordinator
{
    private IQueryable<ReservationResourceState> Rows => db.Set<ReservationResourceState>().AsNoTracking();

    public async Task<ReservationResourceStamp> ReadAsync(ReservationResourceKey key, CancellationToken ct)
    {
        if (key.WorkspaceId == Guid.Empty || string.IsNullOrWhiteSpace(key.Id) || System.Text.Encoding.UTF8.GetByteCount(key.Id) > 1500 || key.Id.Contains('/'))
            throw new DomainException("Identidad de recurso inválida.");
        var kind = key.Kind.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ReservationResourceStates" ("WorkspaceId", "Kind", "ResourceId", "Version", "NextAttemptAt")
            VALUES ({key.WorkspaceId}, {kind}, {key.Id}, 0, {clock.UtcNow}) ON CONFLICT DO NOTHING
            """, ct);
        var row = await Rows.SingleAsync(r => r.WorkspaceId == key.WorkspaceId && r.Kind == kind && r.ResourceId == key.Id, ct);
        return new(key, row.Version, row.PendingOperationId);
    }

    private Task<ReservationResourceState> LockAsync(ReservationResourceKey key, CancellationToken ct) =>
        db.Set<ReservationResourceState>().FromSqlInterpolated($"""
            SELECT * FROM "ReservationResourceStates"
            WHERE "WorkspaceId" = {key.WorkspaceId} AND "Kind" = {key.Kind.ToString()} AND "ResourceId" = {key.Id}
            FOR UPDATE
            """).AsNoTracking().SingleAsync(ct);

    public async Task ValidateAsync(IReadOnlyList<ReservationResourceStamp> observed, CancellationToken ct)
    {
        if (System.Transactions.Transaction.Current == null && db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Reservation resource validation requires a SQL transaction.");
        // One stable order for every booking: Location then Service, ordinal IDs.
        foreach (var stamp in observed.OrderBy(s => s.Key.Kind).ThenBy(s => s.Key.Id, StringComparer.Ordinal))
        {
            var row = await LockAsync(stamp.Key, ct);
            if (row.PendingOperationId != null || row.Version != stamp.Version || stamp.PendingOperationId != null)
                throw new ConcurrencyException("La sede o el servicio cambió. Vuelve a consultar su disponibilidad.");
        }
    }

    public async Task<ReservationResourceMutation> BeginAsync(ReservationResourceStamp observed, ReservationResourceChange change, CancellationToken ct)
    {
        var key = observed.Key; var json = JsonSerializer.Serialize(change);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        var row = await LockAsync(key, ct);
        if (row.PendingOperationId != null)
        {
            // jsonb canonicalizes whitespace/property order: compare parsed content, not raw text.
            using var pendingPayload = JsonDocument.Parse(row.PayloadJson!);
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(change), pendingPayload.RootElement))
                throw new ConcurrencyException("Hay una modificación pendiente de esta sede o servicio.");
            await tx.CommitAsync(ct);
            return Mutation(row);
        }
        if (row.Version != observed.Version) throw new ConcurrencyException("El recurso cambió. Vuelve a consultarlo.");
        var future = db.Reservations.AsNoTracking().Where(r => r.WorkspaceId == key.WorkspaceId
            && r.Status == ReservationStatus.Confirmed && r.StartTime >= clock.UtcNow);
        if (key.Kind == ReservationResourceKind.Location)
            future = future.Where(r => r.LocationId == key.Id);
        else
        {
            future = future.Where(r => r.ServiceId == key.Id);
            if (!change.BlocksAllFuture && change.RetainedLocations != null)
                future = future.Where(r => !change.RetainedLocations.Contains(r.LocationId));
            else if (!change.BlocksAllFuture) future = future.Where(r => false);
        }
        if (await future.AnyAsync(ct)) throw new ConcurrencyException("El cambio afecta reservas futuras confirmadas.");
        var version = checked(row.Version + 1); var id = Guid.NewGuid(); var kind = key.Kind.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ReservationResourceStates" SET "Version" = {version}, "PendingOperationId" = {id},
                "PayloadJson" = CAST({json} AS jsonb), "NextAttemptAt" = {clock.UtcNow}
            WHERE "WorkspaceId" = {key.WorkspaceId} AND "Kind" = {kind} AND "ResourceId" = {key.Id}
            """, ct);
        await tx.CommitAsync(ct);
        return new(key, version, id, json);
    }

    public Task CompleteAsync(ReservationResourceMutation mutation, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE "ReservationResourceStates" SET "PendingOperationId" = NULL, "PayloadJson" = NULL
        WHERE "WorkspaceId" = {mutation.Key.WorkspaceId} AND "Kind" = {mutation.Key.Kind.ToString()}
            AND "ResourceId" = {mutation.Key.Id} AND "Version" = {mutation.Version} AND "PendingOperationId" = {mutation.OperationId}
        """, ct);

    public async Task<IReadOnlyList<ReservationResourceMutation>> ClaimPendingAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var candidates = await Rows.Where(r => r.PendingOperationId != null && r.NextAttemptAt <= now)
            .OrderBy(r => r.NextAttemptAt).Take(10).ToListAsync(ct);
        var result = new List<ReservationResourceMutation>();
        foreach (var row in candidates)
        {
            var changed = await db.Set<ReservationResourceState>().Where(r => r.WorkspaceId == row.WorkspaceId && r.Kind == row.Kind
                && r.ResourceId == row.ResourceId && r.PendingOperationId == row.PendingOperationId && r.NextAttemptAt <= now)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.NextAttemptAt, now.AddSeconds(30)), ct);
            if (changed == 1) result.Add(Mutation(row));
        }
        return result;
    }

    private static ReservationResourceMutation Mutation(ReservationResourceState row) => new(
        new(row.WorkspaceId, Enum.Parse<ReservationResourceKind>(row.Kind), row.ResourceId), row.Version, row.PendingOperationId!.Value, row.PayloadJson!);
}
