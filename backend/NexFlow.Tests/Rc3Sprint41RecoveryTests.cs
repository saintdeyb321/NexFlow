using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.Reservation;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.LocationAvailability;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Workers;
using NexFlow.Tests.Fakes;
using Xunit;
using Fixture = NexFlow.Tests.Rc3Sprint4IntegrityTests.Fixture;

namespace NexFlow.Tests;

public sealed class Rc3Sprint41RecoveryTests
{
    [Theory]
    [InlineData(false, "before-sql")] [InlineData(true, "before-sql")]
    [InlineData(false, "sql-ack")] [InlineData(true, "sql-ack")]
    [InlineData(false, "firestore-ack")] [InlineData(true, "firestore-ack")]
    [InlineData(false, "sql-complete")] [InlineData(true, "sql-complete")]
    [InlineData(false, "sql-complete-ack")] [InlineData(true, "sql-complete-ack")]
    public async Task Restart_reconciles_each_uncertain_boundary_without_unlocking_early(bool location, string stage)
    {
        var f = await Fixture.Create(); var resources = f.Db.Resources;
        if (stage == "before-sql") resources.BeforeBeginCommit = _ => new IOException("SQL not committed");
        if (stage == "sql-ack") resources.AfterBeginCommit = _ => new IOException("SQL committed, ACK lost");
        if (stage == "firestore-ack") f.Db.AfterCommit = _ => new IOException("Firestore committed, ACK lost");
        if (stage == "sql-complete") resources.BeforeComplete = _ => new IOException("SQL unavailable after Firestore commit");
        if (stage == "sql-complete-ack") resources.AfterComplete = _ => new IOException("SQL completion committed, ACK lost");
        await Assert.ThrowsAsync<IOException>(() => Delete(f, location));
        var pending = await resources.ClaimPendingAsync(default);
        Assert.Equal(stage is "before-sql" or "sql-complete-ack" ? 0 : 1, pending.Count);
        if (pending.Count > 0)
        {
            Assert.True((await f.Book()).IsFailure);
            Assert.Empty(f.Rows);
        }
        resources.BeforeBeginCommit = null; resources.AfterBeginCommit = null; resources.BeforeComplete = null; resources.AfterComplete = null; f.Db.AfterCommit = null;
        if (stage == "before-sql") await Delete(f, location);
        else { await Recover(f); await Recover(f); }
        Assert.Empty(await resources.ClaimPendingAsync(default)); Assert.Empty(f.Rows);
        Assert.True((await f.Book()).IsFailure);
        if (location) Assert.DoesNotContain(await f.Locations.GetLocationsAsync(f.Workspace, default), l => l.Id == "location");
        else Assert.False((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Booking_that_read_active_firestore_before_modification_cannot_commit_its_old_observation(bool location)
    {
        var f = await Fixture.Create(); var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Db.Resources.BeforeValidate = async () => { read.SetResult(); await resume.Task; };
        var booking = f.Book("inbound"); await read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await DeleteReplica(f, location); resume.SetResult();
        var result = await booking;
        Assert.Equal("Reservation.ConcurrencyConflict", result.Error.Code); Assert.Empty(f.Rows);
        Assert.Empty(await f.Db.Resources.ClaimPendingAsync(default));
        if (location) Assert.DoesNotContain(await f.Locations.GetLocationsAsync(f.Workspace, default), l => l.Id == "location");
        else Assert.False((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Pending_intent_blocks_booking_until_recovery_even_while_firestore_is_unavailable(bool location)
    {
        var f = await Fixture.Create(); f.Db.BeforeCommit = _ => new IOException("Firestore unavailable");
        await Assert.ThrowsAsync<IOException>(() => Delete(f, location));
        Assert.Single(await f.Db.Resources.ClaimPendingAsync(default));
        Assert.True((await f.Book()).IsFailure); Assert.Empty(f.Rows);
        f.Db.BeforeCommit = null;
        await Recover(f); Assert.Empty(await f.Db.Resources.ClaimPendingAsync(default));
        Assert.True((await f.Book()).IsFailure); Assert.Empty(f.Rows);
    }

    [Fact]
    public async Task Duplicate_recovery_and_old_fence_cannot_deactivate_a_newly_reactivated_service()
    {
        var f = await Fixture.Create(); f.Db.Resources.AfterBeginCommit = _ => new IOException("API stopped after intent commit");
        await Assert.ThrowsAsync<IOException>(() => Delete(f, false)); f.Db.Resources.AfterBeginCommit = null;
        var operation = Assert.Single(await f.Db.Resources.ClaimPendingAsync(default));
        var writes = 0; f.Db.AfterCommit = commit => { if (commit.Writes.Any(w => w.Update?.Name.EndsWith("/catalogItems/service") == true)) Interlocked.Increment(ref writes); return null; };
        var a = new FirestoreCatalogRepository(f.Db.Db, new InMemoryReservationResourceCoordinator(f.Db.Resources.Durable));
        var b = new FirestoreCatalogRepository(f.Db.Db, new InMemoryReservationResourceCoordinator(f.Db.Resources.Durable));
        var fence = $"workspaces/{f.Workspace}/reservationResourceFences/Service_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("service"u8.ToArray()));
        f.Db.SynchronizeReads(fence);
        await Task.WhenAll(a.ReconcileMutationAsync(operation, default), b.ReconcileMutationAsync(operation, default));
        Assert.Equal(1, writes); Assert.Empty(await f.Db.Resources.ClaimPendingAsync(default));
        await b.SaveItemAsync(f.Workspace, Fixture.Service(), false, default);
        var ready = await f.Db.Resources.ReadAsync(operation.Key, default);
        await Assert.ThrowsAsync<ConcurrencyException>(() => a.ReconcileMutationAsync(operation, default));
        Assert.True((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive);
        Assert.Equal(ready, await f.Db.Resources.ReadAsync(operation.Key, default));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Retrying_a_completed_delete_does_not_create_another_operation_or_advance_its_epoch(bool location)
    {
        var f = await Fixture.Create(); await Delete(f, location);
        var key = new ReservationResourceKey(f.Workspace, location ? ReservationResourceKind.Location : ReservationResourceKind.Service, location ? "location" : "service");
        var ready = await f.Db.Resources.ReadAsync(key, default);
        await DeleteReplica(f, location);
        Assert.Equal(ready, await f.Db.Resources.ReadAsync(key, default));
        Assert.Empty(await f.Db.Resources.ClaimPendingAsync(default));
        Assert.True((await f.Book()).IsFailure); Assert.Empty(f.Rows);
    }

    [Fact]
    public async Task Pending_critical_change_does_not_overwrite_a_later_noncritical_edit()
    {
        var f = await Fixture.Create(); var duration = Fixture.Service(); duration.DurationInMinutes = 60;
        f.Db.Resources.AfterBeginCommit = _ => new IOException("Stop before Firestore");
        await Assert.ThrowsAsync<IOException>(() => f.Catalog.SaveItemAsync(f.Workspace, duration, false, default));
        f.Db.Resources.AfterBeginCommit = null;
        var operation = Assert.Single(await f.Db.Resources.ClaimPendingAsync(default));
        var rename = Fixture.Service(); rename.Name = "Concurrent name"; rename.PriceMinorUnits = 1234;
        await f.Catalog.SaveItemAsync(f.Workspace, rename, false, default);
        var error = await Record.ExceptionAsync(() => f.Catalog.ReconcileMutationAsync(operation, default));
        var persisted = (NexFlow.Application.Features.Services.DTOs.ServiceDto)(await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!;
        Assert.Equal("Concurrent name", persisted.Name); Assert.Equal(1234, persisted.PriceMinorUnits); Assert.Equal(30, persisted.DurationInMinutes);
        Assert.IsType<ConcurrencyException>(error); Assert.Empty(await f.Db.Resources.ClaimPendingAsync(default));
        Assert.True((await f.Book()).IsSuccess);
    }

    [Fact]
    public async Task Concurrent_critical_and_innocuous_transactions_cannot_silently_overwrite_each_other()
    {
        var f = await Fixture.Create(); var critical = Fixture.Service(); critical.DurationInMinutes = 60;
        var harmless = Fixture.Service(); harmless.Name = "Concurrent name";
        f.Db.SynchronizeReads($"workspaces/{f.Workspace}/catalogItems/service");
        var results = await Task.WhenAll(Record.ExceptionAsync(() => f.Catalog.SaveItemAsync(f.Workspace, critical, false, default)),
            Record.ExceptionAsync(() => f.Catalog.SaveItemAsync(f.Workspace, harmless, false, default)));
        Assert.Single(results, r => r == null); Assert.Single(results, r => r is ConcurrencyException);
        var persisted = (NexFlow.Application.Features.Services.DTOs.ServiceDto)(await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!;
        if (results[0] == null) { Assert.Equal(60, persisted.DurationInMinutes); Assert.Equal("Service", persisted.Name); }
        else { Assert.Equal(30, persisted.DurationInMinutes); Assert.Equal("Concurrent name", persisted.Name); }
        Assert.Empty(await f.Db.Resources.ClaimPendingAsync(default));
    }

    [Fact]
    public async Task Host_cancellation_leaves_pending_intent_for_the_next_worker()
    {
        var f = await Fixture.Create(); f.Db.Resources.AfterBeginCommit = _ => new IOException("API interrupted");
        await Assert.ThrowsAsync<IOException>(() => Delete(f, false)); f.Db.Resources.AfterBeginCommit = null;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var resources = new InMemoryReservationResourceCoordinator(f.Db.Resources.Durable);
        using var provider = new ServiceCollection().AddSingleton<IReservationResourceCoordinator>(resources).AddSingleton(f.Db.Db)
            .AddSingleton(new FirestoreCatalogRepository(f.Db.Db, resources)).AddSingleton(new FirestoreLocationRepository(f.Db.Db, resources)).BuildServiceProvider();
        var worker = new ReservationResourceRecoveryWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ReservationResourceRecoveryWorker>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RecoverOnceAsync(canceled.Token));
        Assert.Single(await resources.ClaimPendingAsync(default));
        Assert.True((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive);
        await worker.RecoverOnceAsync(default); Assert.Empty(await resources.ClaimPendingAsync(default));
        Assert.False((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive);
    }

    [Fact]
    public async Task Permanent_invalid_payload_is_sealed_rejected_instead_of_retried_forever()
    {
        var f = await Fixture.Create(); var key = new ReservationResourceKey(f.Workspace, ReservationResourceKind.Service, "service");
        await f.Db.Resources.BeginAsync(await f.Db.Resources.ReadAsync(key, default), new("UnsupportedAction"), default);
        await Recover(f); await Recover(f);
        Assert.Empty(await f.Db.Resources.ClaimPendingAsync(default));
        Assert.True((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive);
        Assert.True((await f.Book()).IsSuccess); Assert.Single(f.Rows);
    }

    [Fact]
    public async Task Corrupt_identity_stays_blocked_until_administrative_repair_preserves_the_original_operation()
    {
        var f = await Fixture.Create(); f.Db.Resources.AfterBeginCommit = _ => new IOException("API interrupted");
        await Assert.ThrowsAsync<IOException>(() => Delete(f, false)); f.Db.Resources.AfterBeginCommit = null;
        var original = Assert.Single(await f.Db.Resources.ClaimPendingAsync(default));
        // Fault injection into the SQL-boundary double only; never modifies a real database.
        var row = f.Db.Resources.Durable.Rows[original.Key]; row.Pending = original with { OperationId = Guid.Empty };
        await Recover(f);
        Assert.Single(await f.Db.Resources.ClaimPendingAsync(default)); Assert.True((await f.Book()).IsFailure);
        Assert.True((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive);
        // Model reviewed repair from the original durable intent, keeping its epoch and payload.
        row.Pending = original; await Recover(f);
        Assert.Empty(await f.Db.Resources.ClaimPendingAsync(default));
        Assert.False((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive); Assert.Empty(f.Rows);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Pending_service_does_not_block_an_independent_resource_or_workspace(bool otherWorkspace)
    {
        var f = await Fixture.Create(); var workspace = otherWorkspace ? Guid.NewGuid() : f.Workspace;
        var service = Fixture.Service(); if (!otherWorkspace) service.Id = "other-service";
        if (otherWorkspace) Assert.True((await f.Locations.SaveLocationAsync(workspace, new("location", "Other", "Address", null, null, true), true, 3, default)).IsSuccess);
        await f.Catalog.SaveItemAsync(workspace, service, true, default);
        f.Db.Resources.AfterBeginCommit = _ => new IOException("Pending source service");
        await Assert.ThrowsAsync<IOException>(() => Delete(f, false)); f.Db.Resources.AfterBeginCommit = null;
        f.Reservations.Setup(r => r.IsTimeSlotAvailableAsync(workspace, "location", It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(true);
        var engine = Engine(f);
        var result = await engine.CreateReservationAsync(workspace, "location", service.Id, "customer", "Customer", Fixture.Day.AddHours(15), default, "independent");
        Assert.True(result.IsSuccess, result.Error.Description); Assert.Equal(workspace, Assert.Single(f.Rows).WorkspaceId);
        Assert.Single(await f.Db.Resources.ClaimPendingAsync(default));
    }

    [Fact]
    public async Task Concurrent_web_and_whatsapp_bookings_share_guards_and_replay_identity()
    {
        var f = await Fixture.Create();
        f.Reservations.Setup(r => r.IsTimeSlotAvailableAsync(f.Workspace, "location", It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default))
            .ReturnsAsync(() => f.Rows.Count == 0);
        f.Reservations.Setup(r => r.GetBySourceMessageIdAsync(f.Workspace, It.IsAny<string>(), default))
            .ReturnsAsync((Guid _, string source, CancellationToken _) => f.Rows.SingleOrDefault(r => r.SourceMessageId == source));
        var replica = Engine(f);
        var results = await Task.WhenAll(Task.Run(() => f.Book("whatsapp")), Task.Run(() => replica.CreateReservationAsync(f.Workspace,
            "location", "service", "web", "Web", Fixture.Day.AddHours(15), default, "web-request")));
        Assert.Single(results, r => r.IsSuccess); Assert.Single(f.Rows);
        var winner = f.Rows[0]; var replay = await f.Engine.CreateReservationAsync(f.Workspace, "location", "service", winner.CustomerIdentifier,
            winner.CustomerName, Fixture.Day.AddHours(15), default, winner.SourceMessageId);
        Assert.True(replay.IsSuccess); Assert.Equal(winner.Id, replay.Value.Id); Assert.Single(f.Rows);
    }

    [Fact]
    public void Migration_sql_and_snapshot_preserve_history_and_explicitly_refuse_unsafe_down()
    {
        using var db = new NexFlowDbContextFactory().CreateDbContext(["--offline"]);
        Assert.False(db.Database.HasPendingModelChanges());
        var migrations = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();
        var target = Array.FindIndex(migrations, m => m.EndsWith("_CoordinateReservationResourcesAndReuseCancelledSlots", StringComparison.Ordinal));
        Assert.True(target > 0); var migrator = db.GetService<IMigrator>();
        var up = migrator.GenerateScript(migrations[target - 1], migrations[target]);
        Assert.Contains("WHERE \"Status\" <> 'Cancelled'", up);
        Assert.Contains("CREATE TABLE \"ReservationResourceStates\"", up);
        Assert.DoesNotContain("DELETE FROM", up); Assert.DoesNotContain("UPDATE \"Reservations\"", up);
        var down = migrator.GenerateScript(migrations[target], migrations[target - 1]).Replace("\r\n", "\n");
        Assert.Contains("END;\n$guard$;", down);
        Assert.True(down.IndexOf("RAISE EXCEPTION", StringComparison.Ordinal) < down.IndexOf("DROP TABLE", StringComparison.Ordinal));
        Assert.Contains("EXISTS (SELECT 1 FROM \"ReservationResourceStates\")", down);
        Assert.Contains("HAVING COUNT(*) > 1", down);
        Assert.All(down.Split('\n').Where(line => line.Contains("DELETE FROM", StringComparison.Ordinal)),
            line => Assert.StartsWith("DELETE FROM \"__EFMigrationsHistory\"", line.Trim()));
        Assert.DoesNotContain("UPDATE \"Reservations\"", down);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    private static Task Delete(Fixture f, bool location) => location
        ? f.Locations.DeleteLocationAsync(f.Workspace, "location", default) : f.Catalog.DeleteItemAsync(f.Workspace, "service", default);
    private static Task DeleteReplica(Fixture f, bool location)
    {
        var coordinator = new InMemoryReservationResourceCoordinator(f.Db.Resources.Durable);
        return location ? new FirestoreLocationRepository(f.Db.Db, coordinator).DeleteLocationAsync(f.Workspace, "location", default)
            : new FirestoreCatalogRepository(f.Db.Db, coordinator).DeleteItemAsync(f.Workspace, "service", default);
    }
    private static async Task Recover(Fixture f)
    {
        var resources = new InMemoryReservationResourceCoordinator(f.Db.Resources.Durable);
        using var provider = new ServiceCollection().AddSingleton<IReservationResourceCoordinator>(resources)
            .AddSingleton(f.Db.Db)
            .AddSingleton(new FirestoreCatalogRepository(f.Db.Db, resources)).AddSingleton(new FirestoreLocationRepository(f.Db.Db, resources)).BuildServiceProvider();
        await new ReservationResourceRecoveryWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ReservationResourceRecoveryWorker>.Instance).RecoverOnceAsync(default);
    }
    private static ReservationEngine Engine(Fixture f)
    {
        var resources = new InMemoryReservationResourceCoordinator(f.Db.Resources.Durable);
        var hours = new Mock<IBusinessHoursRepository>(); hours.Setup(h => h.GetBusinessHoursAsync(It.IsAny<Guid>(), "location", default))
            .ReturnsAsync(new[] { new BusinessHoursDto(2, "09:00", "18:00", false) });
        var profile = new Mock<IBusinessProfileRepository>(); profile.Setup(p => p.GetProfileAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(new BusinessProfileDto("Business", "", "", "", "", "America/Lima"));
        var uow = new Mock<IUnitOfWork>(); uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        var catalog = new FirestoreCatalogRepository(f.Db.Db, resources); var locations = new FirestoreLocationRepository(f.Db.Db, resources);
        return new(f.Reservations.Object, catalog, hours.Object, profile.Object, locations, uow.Object,
            new LocationAvailabilityService(catalog, locations), Mock.Of<IClock>(c => c.UtcNow == Fixture.Day.AddDays(-1)), Mock.Of<IOutboxRepository>(), resources);
    }
}
