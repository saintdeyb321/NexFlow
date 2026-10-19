using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Integration;

[Trait("Category", "Integration")]
[Collection("RC3 isolated stores")]
public sealed class PostgresResourceIntegrationTests(ITestOutputHelper output)
{
    [IsolatedIntegrationFact]
    public async Task Two_bookings_same_slot_commit_one_reservation_and_one_outbox()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var a = f.Db(); await using var b = f.Db(); using var gate = new AsyncGate();
        var pa = new CoordinatorProbe(f.Coordinator(a)) { BeforeValidate = gate.Stop };
        var first = f.Book(a, ws, "first", pa); await gate.Reached;
        var second = await f.Book(b, ws, "second"); gate.Open(); var result = await first;
        Assert.True(second.IsSuccess); Assert.True(result.IsFailure);
        await using var check = f.Db(); Assert.Single(await check.Reservations.ToListAsync(f.Ct));
        Assert.Single(await check.OutboxMessages.ToListAsync(f.Ct));
    }

    [IsolatedIntegrationFact]
    public async Task Booking_holds_resource_lock_until_ambient_commit_and_deactivation_waits()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        using var gate = new AsyncGate(); var block = new SaveGate(gate);
        await using var a = f.Db(block); await using var b = f.Db();
        await b.Database.OpenConnectionAsync(f.Ct); var pid = ((NpgsqlConnection)b.Database.GetDbConnection()).ProcessID;
        var booking = f.Book(a, ws, "winner"); await gate.Reached;
        var deletion = new FirestoreCatalogRepository(f.Firestore, f.Coordinator(b)).DeleteItemAsync(ws, "service", f.Ct);
        await f.WaitForBlocked(pid); Assert.False(deletion.IsCompleted);
        gate.Open(); Assert.True((await booking).IsSuccess);
        await Assert.ThrowsAsync<ConcurrencyException>(() => deletion);
        Assert.True((await new FirestoreCatalogRepository(f.Firestore, f.Coordinator(b)).GetItemByIdAsync(ws, "service", f.Ct))!.IsActive);
        await using var check = f.Db(); Assert.Single(await check.Reservations.ToListAsync(f.Ct)); Assert.Single(await check.OutboxMessages.ToListAsync(f.Ct));
    }

    [IsolatedIntegrationFact]
    public async Task Deactivation_wins_against_booking_with_old_firestore_snapshot()
    {
        await CheckDeletionWins(false);
    }

    [IsolatedIntegrationFact]
    public async Task Location_deletion_wins_against_booking_with_old_firestore_snapshot()
    {
        await CheckDeletionWins(true);
    }

    private static async Task CheckDeletionWins(bool location)
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var a = f.Db(); await using var b = f.Db(); using var gate = new AsyncGate();
        var probe = new CoordinatorProbe(f.Coordinator(a)) { BeforeValidate = gate.Stop };
        var booking = f.Book(a, ws, "stale", probe); await gate.Reached;
        if (location) Assert.True((await new FirestoreLocationRepository(f.Firestore, f.Coordinator(b)).DeleteLocationAsync(ws, "location", f.Ct)).IsSuccess);
        else await new FirestoreCatalogRepository(f.Firestore, f.Coordinator(b)).DeleteItemAsync(ws, "service", f.Ct);
        gate.Open(); Assert.Equal("Reservation.ConcurrencyConflict", (await booking).Error.Code);
        await using var check = f.Db(); Assert.Empty(await check.Reservations.ToListAsync(f.Ct)); Assert.Empty(await check.OutboxMessages.ToListAsync(f.Ct));
        Assert.Null((await f.Coordinator(check).ReadAsync(f.Key(ws, location), f.Ct)).PendingOperationId);
    }

    [IsolatedIntegrationFact]
    public async Task Rescheduling_conflict_preserves_both_intervals_and_outbox()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var a = f.Db(); var one = await f.Book(a, ws, "one");
        await using var b = f.Db(); var two = await f.Book(b, ws, "two", minute: 60);
        Assert.True(one.IsSuccess); Assert.True(two.IsSuccess);
        await using var edit = f.Db(); var result = await f.Engine(edit).EditReservationAsync(ws, two.Value.Id, IsolatedStores.Day.AddHours(15), f.Ct);
        Assert.True(result.IsFailure);
        await using var check = f.Db(); var rows = await check.Reservations.OrderBy(r => r.StartTime).ToListAsync(f.Ct);
        Assert.Equal(2, rows.Count); Assert.Equal(IsolatedStores.Day.AddHours(16), rows[1].StartTime);
        Assert.Equal(2, await check.OutboxMessages.CountAsync(f.Ct));
    }

    [IsolatedIntegrationFact]
    public async Task Serializable_snapshot_conflict_is_real_40001_and_rollback_preserves_winner()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(false);
        await using var seed = f.Db(); var key = f.Key(ws); await f.Coordinator(seed).ReadAsync(key, f.Ct);
        await using var a = new NpgsqlConnection(f.ConnectionString); await using var b = new NpgsqlConnection(f.ConnectionString);
        await a.OpenAsync(f.Ct); await b.OpenAsync(f.Ct);
        await using var ta = await a.BeginTransactionAsync(IsolationLevel.Serializable, f.Ct);
        await using var tb = await b.BeginTransactionAsync(IsolationLevel.Serializable, f.Ct);
        const string read = "SELECT \"Version\" FROM \"ReservationResourceStates\"";
        await new NpgsqlCommand(read, a, ta).ExecuteScalarAsync(f.Ct); await new NpgsqlCommand(read, b, tb).ExecuteScalarAsync(f.Ct);
        const string update = "UPDATE \"ReservationResourceStates\" SET \"Version\" = \"Version\" + 1";
        await new NpgsqlCommand(update, a, ta).ExecuteNonQueryAsync(f.Ct); await ta.CommitAsync(f.Ct);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(update, b, tb).ExecuteNonQueryAsync(f.Ct));
        Assert.Equal("40001", ex.SqlState); await tb.RollbackAsync(f.Ct);
        Assert.Equal(1, (await f.Coordinator(seed).ReadAsync(key, f.Ct)).Version);
    }

    [IsolatedIntegrationFact]
    public async Task Actual_xmin_rejects_stale_update_and_preserves_cancellation()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var seed = f.Db(); var booked = await f.Book(seed, ws, "xmin"); Assert.True(booked.IsSuccess);
        await using var a = f.Db(); await using var b = f.Db();
        var first = await a.Reservations.SingleAsync(f.Ct); var stale = await b.Reservations.SingleAsync(f.Ct);
        var version = first.RowVersion; Assert.NotEqual(0u, version); Assert.Equal(version, stale.RowVersion);
        first.Cancel(); await a.SaveChangesAsync(f.Ct); Assert.NotEqual(version, first.RowVersion);
        stale.Reschedule(stale.StartTime.AddHours(1), stale.EndTime.AddHours(1));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync(f.Ct));
        await using var check = f.Db(); Assert.Equal(NexFlow.Domain.Enums.ReservationStatus.Cancelled, (await check.Reservations.SingleAsync(f.Ct)).Status);
        Assert.Single(await check.OutboxMessages.ToListAsync(f.Ct));
    }

    [IsolatedIntegrationFact]
    public async Task Reservation_and_outbox_roll_back_together_then_source_replay_is_unique()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using (var faulty = f.Db(new FailAfterSave()))
            await Assert.ThrowsAsync<IOException>(() => f.Book(faulty, ws, "atomic"));
        await using (var check = f.Db()) { Assert.Empty(await check.Reservations.ToListAsync(f.Ct)); Assert.Empty(await check.OutboxMessages.ToListAsync(f.Ct)); }
        await using var retry = f.Db(); var first = await f.Book(retry, ws, "atomic"); Assert.True(first.IsSuccess);
        await using var restart = f.Db(); var replay = await f.Book(restart, ws, "atomic");
        Assert.True(replay.IsSuccess); Assert.Equal(first.Value.Id, replay.Value.Id);
        Assert.Single(await restart.Reservations.ToListAsync(f.Ct)); Assert.Single(await restart.OutboxMessages.ToListAsync(f.Ct));
    }

    [IsolatedIntegrationFact]
    public async Task Ordered_locks_avoid_deadlocks_and_other_workspace_is_independent()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(false); var other = await f.Workspace(false);
        await using var a = f.Db(); await using var b = f.Db(); await using var c = f.Db();
        var stamps = new[] { await f.Coordinator(a).ReadAsync(f.Key(ws), f.Ct), await f.Coordinator(a).ReadAsync(f.Key(ws, true), f.Ct) };
        var independent = await f.Coordinator(c).ReadAsync(f.Key(other), f.Ct);
        await using var ta = await a.Database.BeginTransactionAsync(f.Ct);
        await f.Coordinator(a).ValidateAsync(stamps, f.Ct);
        await b.Database.OpenConnectionAsync(f.Ct); var pid = ((NpgsqlConnection)b.Database.GetDbConnection()).ProcessID;
        await using var tb = await b.Database.BeginTransactionAsync(f.Ct);
        var waiting = f.Coordinator(b).ValidateAsync(stamps.Reverse().ToArray(), f.Ct);
        await f.WaitForBlocked(pid);
        await using (var tc = await c.Database.BeginTransactionAsync(f.Ct))
        { await f.Coordinator(c).ValidateAsync([independent], f.Ct); await tc.CommitAsync(f.Ct); }
        await ta.CommitAsync(f.Ct); await waiting.WaitAsync(TimeSpan.FromSeconds(5)); await tb.CommitAsync(f.Ct);
        output.WriteLine("Two independent backends; ordered resource locks released at commit; unrelated tenant completed while contender waited.");
    }

    [IsolatedIntegrationFact]
    public async Task Deliberate_inverted_lock_order_reports_40P01_and_survivor_can_commit()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(false);
        await using var seed = f.Db(); await f.Coordinator(seed).ReadAsync(f.Key(ws), f.Ct); await f.Coordinator(seed).ReadAsync(f.Key(ws, true), f.Ct);
        await using var a = new NpgsqlConnection(f.ConnectionString); await using var b = new NpgsqlConnection(f.ConnectionString);
        await a.OpenAsync(f.Ct); await b.OpenAsync(f.Ct);
        await using var ta = await a.BeginTransactionAsync(f.Ct); await using var tb = await b.BeginTransactionAsync(f.Ct);
        async Task Lock(NpgsqlConnection c, string kind)
        {
            await using var command = new NpgsqlCommand("SELECT * FROM \"ReservationResourceStates\" WHERE \"Kind\" = @kind FOR UPDATE", c);
            command.Parameters.AddWithValue("kind", kind); await command.ExecuteNonQueryAsync(f.Ct);
        }
        await Lock(a, "Location"); await Lock(b, "Service");
        var waitingA = Lock(a, "Service"); await f.WaitForBlocked(a.ProcessID);
        var waitingB = Lock(b, "Location");
        async Task<bool> Finish(Task work, NpgsqlTransaction tx)
        {
            try { await work; await tx.CommitAsync(f.Ct); return true; }
            catch (PostgresException ex) { Assert.Equal("40P01", ex.SqlState); await tx.RollbackAsync(f.Ct); return false; }
        }
        var results = await Task.WhenAll(Finish(waitingA, ta), Finish(waitingB, tb)).WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Single(results, success => success); Assert.Single(results, success => !success);
        var rows = await seed.Set<NexFlow.Domain.Entities.System.ReservationResourceState>().AsNoTracking().ToListAsync(f.Ct);
        Assert.Equal(2, rows.Count); Assert.All(rows, row => { Assert.Equal(0, row.Version); Assert.Null(row.PendingOperationId); });
        output.WriteLine("Synthetic inverted order: one PostgreSQL 40P01, one survivor, no leaked transaction or altered epoch. Production uses the ordered path tested separately.");
    }

    [IsolatedIntegrationFact]
    public async Task Cancelled_resource_lock_wait_rolls_back_and_next_attempt_succeeds()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(false);
        await using var a = f.Db(); await using var b = f.Db(); var r = f.Coordinator(a);
        var stamp = await r.ReadAsync(f.Key(ws), f.Ct);
        await using var tx = await a.Database.BeginTransactionAsync(f.Ct); await r.ValidateAsync([stamp], f.Ct);
        await b.Database.OpenConnectionAsync(f.Ct); using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(f.Ct);
        var waiting = f.Coordinator(b).BeginAsync(stamp, new("DeactivateService"), cancelled.Token);
        await f.WaitForBlocked(((NpgsqlConnection)b.Database.GetDbConnection()).ProcessID); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Null(b.Database.CurrentTransaction); await tx.RollbackAsync(f.Ct);
        var op = await f.Coordinator(b).BeginAsync(stamp, new("DeactivateService"), f.Ct);
        Assert.Equal(stamp.Version + 1, op.Version);
        Assert.Equal(op.OperationId, (await f.Coordinator(b).ReadAsync(f.Key(ws), f.Ct)).PendingOperationId);
    }

    private sealed class SaveGate(AsyncGate gate) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        { await gate.Stop(ct); return result; }
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default) =>
            throw new IOException("Synthetic failure after SQL writes, before ambient commit.");
    }
}
