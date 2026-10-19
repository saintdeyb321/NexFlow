using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Infrastructure.Workers;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Integration;

[Trait("Category", "Integration")]
[Collection("RC3 isolated stores")]
public sealed class TenantLifecycleIntegrationTests(ITestOutputHelper output)
{
    [IsolatedIntegrationFact]
    public async Task Lost_ack_cancellation_releases_shared_guard_then_real_deletion_worker_purges_and_restart_is_denied()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(); var other = await f.Workspace();
        await using var recovery = f.Db(); await recovery.Database.OpenConnectionAsync(f.Ct);
        var recoveryPid = ((NpgsqlConnection)recovery.Database.GetDbConnection()).ProcessID;
        var r = f.Coordinator(recovery); var op = await r.BeginAsync(await r.ReadAsync(f.Key(ws), f.Ct), new("DeactivateService"), f.Ct);
        using var gate = new AsyncGate(); using var attempt = CancellationTokenSource.CreateLinkedTokenSource(f.Ct);
        var probe = new CoordinatorProbe(r) { BeforeComplete = _ => gate.Stop(attempt.Token) };
        var elapsed = Stopwatch.StartNew();
        // Actual Firestore business+fence commit occurs before this ACK boundary.
        var running = r.ExecuteRecoveryAsync(ws, () => new FirestoreCatalogRepository(f.Firestore, probe).ReconcileMutationAsync(op, attempt.Token), attempt.Token);
        await gate.Reached;
        await using var deletion = f.Db(); await deletion.Database.OpenConnectionAsync(f.Ct);
        var deletionPid = ((NpgsqlConnection)deletion.Database.GetDbConnection()).ProcessID;
        var deleting = ScheduleDeletion(f, deletion, ws);
        await f.WaitForBlocked(deletionPid); Assert.False(deleting.IsCompleted);
        var transactionAgeMs = await AssertActivity(f, recoveryPid, "idle in transaction");
        await using (var independent = f.Db())
            await f.Coordinator(independent).ExecuteRecoveryAsync(other, () => Task.CompletedTask, f.Ct);
        // Accelerate the worker's deadline only after the SQL wait is observed.
        // No sleep is used to guess whether either store committed.
        attempt.CancelAfter(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        var exclusiveWait = await deleting.WaitAsync(TimeSpan.FromSeconds(5)); elapsed.Stop();
        Assert.Null(recovery.Database.CurrentTransaction);
        // SQL completion rolled back; Firestore's terminal result remains durable until purge.
        await using (var check = f.Db())
            Assert.Equal(op.OperationId, (await f.Coordinator(check).ReadAsync(f.Key(ws), f.Ct)).PendingOperationId);
        var services = new ServiceCollection().AddScoped(_ => f.Db()).AddScoped<IWorkspaceRepository, WorkspaceRepository>()
            .AddSingleton<ITenantCleanupService>(new FirestoreTenantCleanupService(f.Firestore, NullLogger<FirestoreTenantCleanupService>.Instance));
        await using var provider = services.BuildServiceProvider();
        using var worker = new TenantDeletionWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TenantDeletionWorker>.Instance);
        await worker.StartAsync(f.Ct);
        try { await WaitForDeletion(f, ws); }
        finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await worker.StopAsync(stop.Token); }
        // A response for the already committed remote operation arrives only
        // after purge. The cancelled continuation must not complete/replay it.
        gate.Open();
        await using var restarted = f.Db(); var recovered = f.Coordinator(restarted); var called = false;
        await Assert.ThrowsAsync<ConcurrencyException>(() => recovered.ExecuteRecoveryAsync(ws, () => { called = true; return Task.CompletedTask; }, f.Ct));
        Assert.False(called); Assert.False((await f.Root(ws).GetSnapshotAsync(f.Ct)).Exists);
        Assert.Empty((await f.Root(ws).Collection("catalogItems").GetSnapshotAsync(f.Ct)).Documents);
        Assert.Empty((await f.Root(ws).Collection("reservationResourceFences").GetSnapshotAsync(f.Ct)).Documents);
        Assert.Empty(await restarted.Set<ReservationResourceState>().Where(s => s.WorkspaceId == ws).ToListAsync(f.Ct));
        Assert.True(await restarted.Workspaces.AnyAsync(w => w.Id == other, f.Ct));
        output.WriteLine($"Measured exclusive lock wait: {exclusiveWait.TotalMilliseconds:F1} ms; " +
            $"open recovery transaction age at ACK boundary: {transactionAgeMs:F1} ms; " +
            $"shared-guard/cancellation window: {elapsed.ElapsedMilliseconds} ms. " +
            "Two occupied transaction connections plus observer; unrelated tenant completed. Pool MaxPoolSize=8. These are test values, not production limits.");
    }

    [IsolatedIntegrationFact]
    public async Task Pool_wait_is_cancelled_and_rolled_back_lifecycle_connection_is_reusable()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(false);
        var cs = new NpgsqlConnectionStringBuilder(f.ConnectionString) { MaxPoolSize = 1, ApplicationName = "nexflow-rc3-pool-probe" }.ConnectionString;
        await using var held = new NpgsqlConnection(cs); await held.OpenAsync(f.Ct);
        await using var waiter = new NpgsqlConnection(cs); using var ct = CancellationTokenSource.CreateLinkedTokenSource(f.Ct);
        var waiting = waiter.OpenAsync(ct.Token); Assert.False(waiting.IsCompleted); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await held.CloseAsync(); await waiter.OpenAsync(f.Ct); Assert.Equal(System.Data.ConnectionState.Open, waiter.State);
        await waiter.CloseAsync(); NpgsqlConnection.ClearPool(waiter);
        await using var db = f.Db(); using var gate = new AsyncGate(); using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Ct);
        var recovery = f.Coordinator(db).ExecuteRecoveryAsync(ws, () => gate.Stop(cancellation.Token), cancellation.Token);
        await gate.Reached; cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recovery);
        Assert.Null(db.Database.CurrentTransaction);
        await f.Coordinator(db).ExecuteRecoveryAsync(ws, () => Task.CompletedTask, f.Ct);
        output.WriteLine("Pool capacity=1: saturated open cancelled; same pool reused; recovery rollback released transaction.");
    }

    private static async Task<TimeSpan> ScheduleDeletion(IsolatedStores f, NexFlowDbContext db, Guid ws)
    {
        await using var tx = await db.Database.BeginTransactionAsync(f.Ct);
        var waiting = Stopwatch.StartNew();
        await TenantLifecycleLock.AcquireAsync(db, ws, true, f.Ct);
        waiting.Stop();
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == ws, f.Ct); workspace.BeginDeletion();
        db.TenantDeletionJobs.Add(new TenantDeletionJob { WorkspaceId = ws, RequestedBy = Guid.NewGuid() });
        await db.SaveChangesAsync(f.Ct); await tx.CommitAsync(f.Ct);
        return waiting.Elapsed;
    }

    private static async Task<decimal> AssertActivity(IsolatedStores f, int pid, string expected)
    {
        await using var observer = new NpgsqlConnection(f.ConnectionString); await observer.OpenAsync(f.Ct);
        await using var command = new NpgsqlCommand("SELECT state FROM pg_stat_activity WHERE pid = @pid AND xact_start IS NOT NULL", observer);
        command.Parameters.AddWithValue("pid", pid); Assert.Equal(expected, await command.ExecuteScalarAsync(f.Ct));
        await using var age = new NpgsqlCommand("SELECT EXTRACT(EPOCH FROM (clock_timestamp() - xact_start)) * 1000 FROM pg_stat_activity WHERE pid = @pid", observer);
        age.Parameters.AddWithValue("pid", pid);
        return Assert.IsType<decimal>(await age.ExecuteScalarAsync(f.Ct));
    }

    private static async Task WaitForDeletion(IsolatedStores f, Guid ws)
    {
        using var ct = CancellationTokenSource.CreateLinkedTokenSource(f.Ct); ct.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            await using var check = f.Db();
            var job = await check.TenantDeletionJobs.AsNoTracking().SingleAsync(j => j.WorkspaceId == ws, ct.Token);
            Assert.NotEqual(TenantDeletionStatus.Failed, job.Status);
            if (job.Status == TenantDeletionStatus.Completed) { Assert.False(await check.Workspaces.AnyAsync(w => w.Id == ws, ct.Token)); return; }
            await Task.Delay(20, ct.Token);
        }
    }
}
