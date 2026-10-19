using Google.Cloud.Firestore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Workers;
using Xunit;

namespace NexFlow.Tests.Integration;

[Trait("Category", "Integration")]
[Collection("RC3 isolated stores")]
public sealed class FirestoreResourceIntegrationTests
{
    [IsolatedIntegrationFact]
    public async Task Business_and_fence_commit_atomically_and_same_identity_does_not_write_twice()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var db = f.Db(); var resources = f.Coordinator(db);
        var op = await resources.BeginAsync(await resources.ReadAsync(f.Key(ws), f.Ct), new("DeactivateService"), f.Ct);
        var repo = new FirestoreCatalogRepository(f.Firestore, resources);
        await repo.ReconcileMutationAsync(op, f.Ct);
        var doc = f.Root(ws).Collection("catalogItems").Document("service");
        var first = await doc.GetSnapshotAsync(f.Ct); Assert.False(first.GetValue<bool>("IsActive"));
        var fence = await new ReservationResourceFence(f.Firestore, resources).Reference(op).GetSnapshotAsync(f.Ct);
        Assert.True(fence.GetValue<bool>("Applied")); Assert.Equal(op.Version, fence.GetValue<long>("Version"));
        await using var restart = f.Db(); await new FirestoreCatalogRepository(f.Firestore, f.Coordinator(restart)).ReconcileMutationAsync(op, f.Ct);
        Assert.Equal(first.UpdateTime, (await doc.GetSnapshotAsync(f.Ct)).UpdateTime);
        Assert.Null((await resources.ReadAsync(f.Key(ws), f.Ct)).PendingOperationId);
    }

    [IsolatedIntegrationFact]
    public async Task Concurrent_same_identity_writers_commit_one_business_revision()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var a = f.Db(); await using var b = f.Db();
        var resources = f.Coordinator(a); var op = await resources.BeginAsync(await resources.ReadAsync(f.Key(ws), f.Ct), new("DeactivateService"), f.Ct);
        // Both independent SDK transactions see the same fence before either writes.
        using var gate = new AsyncGate(); var entered = 0;
        var business = f.Root(ws).Collection("integrationEvidence").Document("counter");
        async Task<Result> Write(Transaction tx)
        {
            var value = await tx.GetSnapshotAsync(business, f.Ct);
            if (Interlocked.Increment(ref entered) == 2) gate.Open();
            await gate.Stop(f.Ct);
            tx.Set(business, new Dictionary<string, object> { ["Count"] = value.Exists ? value.GetValue<long>("Count") + 1 : 1L });
            return Result.Success();
        }
        var results = await Task.WhenAll(new ReservationResourceFence(f.Firestore, resources).ApplyAsync(op, Write, f.Ct),
            new ReservationResourceFence(f.Firestore, f.Coordinator(b)).ApplyAsync(op, Write, f.Ct));
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(1, (await business.GetSnapshotAsync(f.Ct)).GetValue<long>("Count"));
        Assert.Null((await resources.ReadAsync(f.Key(ws), f.Ct)).PendingOperationId);
    }

    [IsolatedIntegrationFact]
    public async Task Lost_ack_after_firestore_commit_recovers_on_new_worker_without_repeating_business()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var a = f.Db(); var coordinator = f.Coordinator(a);
        var probe = new CoordinatorProbe(coordinator) { BeforeComplete = _ => throw new IOException("Synthetic ACK loss after committed Firestore transaction.") };
        await Assert.ThrowsAsync<IOException>(() => new FirestoreCatalogRepository(f.Firestore, probe).DeleteItemAsync(ws, "service", f.Ct));
        var pending = (await coordinator.ReadAsync(f.Key(ws), f.Ct)).PendingOperationId; Assert.NotNull(pending);
        var doc = f.Root(ws).Collection("catalogItems").Document("service"); var committed = await doc.GetSnapshotAsync(f.Ct);
        Assert.False(committed.GetValue<bool>("IsActive"));
        await using var provider = RecoveryProvider(f); var worker = new ReservationResourceRecoveryWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ReservationResourceRecoveryWorker>.Instance);
        await worker.RecoverOnceAsync(f.Ct); await worker.RecoverOnceAsync(f.Ct);
        Assert.Equal(committed.UpdateTime, (await doc.GetSnapshotAsync(f.Ct)).UpdateTime);
        Assert.Null((await coordinator.ReadAsync(f.Key(ws), f.Ct)).PendingOperationId);
    }

    [IsolatedIntegrationFact]
    public async Task Older_callback_cannot_overwrite_new_epoch_or_complete_new_intent()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var a = f.Db(); var r = f.Coordinator(a); var repo = new FirestoreCatalogRepository(f.Firestore, r);
        var old = await r.BeginAsync(await r.ReadAsync(f.Key(ws), f.Ct), new("DeactivateService"), f.Ct);
        await repo.ReconcileMutationAsync(old, f.Ct);
        await repo.SaveItemAsync(ws, IsolatedStores.Service("Reactivated"), false, f.Ct);
        var newer = await r.ReadAsync(f.Key(ws), f.Ct); Assert.True(newer.Version > old.Version);
        var doc = f.Root(ws).Collection("catalogItems").Document("service"); var current = await doc.GetSnapshotAsync(f.Ct);
        await Assert.ThrowsAsync<ConcurrencyException>(() => repo.ReconcileMutationAsync(old, f.Ct));
        await r.CompleteAsync(old, f.Ct);
        Assert.Equal(current.UpdateTime, (await doc.GetSnapshotAsync(f.Ct)).UpdateTime);
        Assert.Equal(newer, await r.ReadAsync(f.Key(ws), f.Ct));
    }

    [IsolatedIntegrationFact]
    public async Task Rejected_terminal_marker_preserves_noncritical_edit_and_history()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var a = f.Db(); var r = f.Coordinator(a); var repo = new FirestoreCatalogRepository(f.Firestore, r);
        var history = f.Root(ws).Collection("history").Document("reservation"); await history.SetAsync(new { Label = "Original", Price = 25 }, cancellationToken: f.Ct);
        var doc = f.Root(ws).Collection("catalogItems").Document("service"); var original = await doc.GetSnapshotAsync(f.Ct);
        var desired = IsolatedStores.Service(); desired.DurationInMinutes = 45;
        var op = await r.BeginAsync(await r.ReadAsync(f.Key(ws), f.Ct), new("SaveService", desired, ExpectedUpdatedAtUtc: original.UpdateTime!.Value.ToDateTime()), f.Ct);
        await using var b = f.Db(); await new FirestoreCatalogRepository(f.Firestore, f.Coordinator(b)).SaveItemAsync(ws, IsolatedStores.Service("Concurrent name"), false, f.Ct);
        await repo.ReconcileMutationAsync(op, f.Ct, throwOnRejected: false);
        var saved = await repo.GetItemByIdAsync(ws, "service", f.Ct);
        Assert.Equal("Concurrent name", saved!.Name);
        Assert.Equal(30, Assert.IsType<NexFlow.Application.Features.Services.DTOs.ServiceDto>(saved).DurationInMinutes);
        var marker = await new ReservationResourceFence(f.Firestore, r).Reference(op).GetSnapshotAsync(f.Ct);
        Assert.False(marker.GetValue<bool>("Applied")); Assert.Equal("Resource.Conflict", marker.GetValue<string>("ErrorCode"));
        Assert.Null((await r.ReadAsync(f.Key(ws), f.Ct)).PendingOperationId);
        Assert.Equal("Original", (await history.GetSnapshotAsync(f.Ct)).GetValue<string>("Label"));
    }

    [IsolatedIntegrationFact]
    public async Task Malformed_payload_is_durably_rejected_by_real_worker()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var a = f.Db(); var r = f.Coordinator(a);
        var op = await r.BeginAsync(await r.ReadAsync(f.Key(ws), f.Ct), new("DeactivateService"), f.Ct);
        await a.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReservationResourceStates\" SET \"PayloadJson\" = '{{}}'::jsonb WHERE \"WorkspaceId\" = {ws}", f.Ct);
        await using var provider = RecoveryProvider(f); var worker = new ReservationResourceRecoveryWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ReservationResourceRecoveryWorker>.Instance);
        await worker.RecoverOnceAsync(f.Ct);
        Assert.Null((await r.ReadAsync(f.Key(ws), f.Ct)).PendingOperationId);
        Assert.True((await new FirestoreCatalogRepository(f.Firestore, r).GetItemByIdAsync(ws, "service", f.Ct))!.IsActive);
        var marker = await new ReservationResourceFence(f.Firestore, r).Reference(op).GetSnapshotAsync(f.Ct);
        Assert.False(marker.GetValue<bool>("Applied")); Assert.Equal("Resource.InvalidPayload", marker.GetValue<string>("ErrorCode"));
    }

    [IsolatedIntegrationFact]
    public async Task Rejected_write_does_not_publish_business_data_and_other_tenant_is_unchanged()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(); var other = await f.Workspace();
        await using var a = f.Db(); var r = f.Coordinator(a);
        var op = await r.BeginAsync(await r.ReadAsync(f.Key(ws), f.Ct), new("DeactivateService"), f.Ct);
        var evidence = f.Root(ws).Collection("integrationEvidence").Document("should-not-exist");
        var fence = new ReservationResourceFence(f.Firestore, r);
        var result = await fence.ApplyAsync(op, tx =>
        { tx.Set(evidence, new { Value = "must roll back" }); throw new DomainException("Synthetic validation failure"); }, f.Ct);
        Assert.True(result.IsFailure); Assert.False((await evidence.GetSnapshotAsync(f.Ct)).Exists);
        Assert.True((await new FirestoreCatalogRepository(f.Firestore, r).GetItemByIdAsync(other, "service", f.Ct))!.IsActive);
        Assert.DoesNotContain((await f.Root(other).Collection("reservationResourceFences").GetSnapshotAsync(f.Ct)).Documents,
            d => d.Id == fence.Reference(op).Id && d.GetValue<long>("Version") == op.Version);
    }

    internal static ServiceProvider RecoveryProvider(IsolatedStores f) => new ServiceCollection()
        .AddSingleton(f.Firestore).AddSingleton(f.Clock)
        .AddScoped(_ => f.Db()).AddScoped<IReservationResourceCoordinator>(sp => f.Coordinator(sp.GetRequiredService<NexFlow.Infrastructure.Persistence.PostgreSQL.Context.NexFlowDbContext>()))
        .AddScoped<FirestoreCatalogRepository>().AddScoped<FirestoreLocationRepository>().BuildServiceProvider();
}
