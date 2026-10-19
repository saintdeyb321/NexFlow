using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Workers;
using NexFlow.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NexFlow.Tests;

// Executes the production coordinator's SQL predicates/CAS against SQLite with a
// transport adapter. FOR UPDATE is recorded then removed, jsonb casts unwrapped.
// This intentionally does NOT prove PG locks, isolation, jsonb or crash durability.
public sealed class Rc3Sprint41SqlBoundaryTests
{
    [Theory]
    [InlineData(ReservationStatus.Confirmed, true)]
    [InlineData(ReservationStatus.Pending, false)]
    [InlineData(ReservationStatus.Completed, false)]
    [InlineData(ReservationStatus.Cancelled, false)]
    public async Task Mutation_guard_uses_only_future_confirmed_reservations_of_its_workspace(ReservationStatus status, bool blocked)
    {
        await using var f = await Fixture.Create();
        var row = Reservation.Create(f.Workspace.Id, "location", "service", "customer", "Customer", f.Now.AddDays(1), f.Now.AddDays(1).AddMinutes(30));
        f.Db.Reservations.Add(row); f.Db.Entry(row).Property(r => r.Status).CurrentValue = status;
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var key = new ReservationResourceKey(f.Workspace.Id, ReservationResourceKind.Service, "service");
        var observed = await f.Resources.ReadAsync(key, default);
        if (blocked) await Assert.ThrowsAsync<ConcurrencyException>(() => f.Resources.BeginAsync(observed, new("DeactivateService"), default));
        else Assert.Equal(1, (await f.Resources.BeginAsync(observed, new("DeactivateService"), default)).Version);
        var other = Workspace.Create("Other"); f.Db.Workspaces.Add(other); await f.Db.SaveChangesAsync();
        var independent = await f.Resources.ReadAsync(key with { WorkspaceId = other.Id }, default);
        Assert.Equal(1, (await f.Resources.BeginAsync(independent, new("DeactivateService"), default)).Version);
        Assert.Single(await f.Db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task Production_coordinator_recovers_identity_claims_and_completion_without_releasing_a_newer_epoch()
    {
        await using var f = await Fixture.Create();
        var key = new ReservationResourceKey(f.Workspace.Id, ReservationResourceKind.Service, "service");
        var before = await f.Resources.ReadAsync(key, default);
        var operation = await f.Resources.BeginAsync(before, new("DeactivateService"), default);
        // A separate DbContext models a new SQL client over the same stored rows.
        await using var restarted = new BoundaryDb(f.Options);
        var other = new ReservationResourceCoordinator(restarted, f.Clock.Object);
        var retry = await other.BeginAsync(await other.ReadAsync(key, default), new("DeactivateService"), default);
        Assert.Equal(operation.OperationId, retry.OperationId); Assert.Equal(operation.Version, retry.Version);
        Assert.Single(await other.ClaimPendingAsync(default)); Assert.Empty(await other.ClaimPendingAsync(default));
        f.Now = f.Now.AddSeconds(31); Assert.Single(await other.ClaimPendingAsync(default));
        await other.CompleteAsync(operation with { OperationId = Guid.NewGuid() }, default);
        Assert.NotNull((await other.ReadAsync(key, default)).PendingOperationId);
        await other.CompleteAsync(operation, default);
        var ready = await other.ReadAsync(key, default); Assert.Null(ready.PendingOperationId); Assert.Equal(1, ready.Version);
        var newer = await other.BeginAsync(ready, new("DeactivateService"), default);
        await f.Resources.CompleteAsync(operation, default);
        Assert.Equal(newer.OperationId, (await other.ReadAsync(key, default)).PendingOperationId);
        await using var tx = await restarted.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<ConcurrencyException>(() => other.ValidateAsync([before], default));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Booking_validation_requires_a_transaction_and_orders_location_before_service()
    {
        await using var f = await Fixture.Create();
        var location = await f.Resources.ReadAsync(new(f.Workspace.Id, ReservationResourceKind.Location, "location"), default);
        var service = await f.Resources.ReadAsync(new(f.Workspace.Id, ReservationResourceKind.Service, "service"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Resources.ValidateAsync([service, location], default));
        f.Adapter.LockKinds.Clear();
        await using var tx = await f.Db.Database.BeginTransactionAsync();
        await f.Resources.ValidateAsync([service, location], default);
        Assert.Equal(new[] { "Location", "Service" }, f.Adapter.LockKinds);
        await tx.RollbackAsync();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Resources.ReadAsync(location.Key, cancellation.Token));
    }

    [Theory]
    [InlineData("active")] [InlineData("deleting")] [InlineData("missing")]
    public async Task Recovery_reuses_tenant_lifecycle_guard_and_does_not_dispatch_for_missing_or_deleting_workspaces(string state)
    {
        await using var f = await Fixture.Create();
        if (state == "deleting") { var row = await f.Db.Workspaces.SingleAsync(); row.BeginDeletion(); await f.Db.SaveChangesAsync(); }
        var called = false;
        Task Apply() { called = true; Assert.NotNull(f.Db.Database.CurrentTransaction); return Task.CompletedTask; }
        if (state == "active") await f.Resources.ExecuteRecoveryAsync(f.Workspace.Id, Apply, default);
        else await Assert.ThrowsAsync<ConcurrencyException>(() => f.Resources.ExecuteRecoveryAsync(state == "missing" ? Guid.NewGuid() : f.Workspace.Id, Apply, default));
        Assert.Equal(state == "active", called); Assert.Null(f.Db.Database.CurrentTransaction);
        Assert.Equal(1, f.Adapter.LifecycleCalls); // Invocation only; SQLite does NOT acquire this PG advisory lock.
    }

    [Fact]
    public async Task Worker_commits_rejected_terminal_completion_without_rolling_it_back_as_a_business_error()
    {
        await using var f = await Fixture.Create(); var firestore = new InMemoryFirestore();
        var catalog = new FirestoreCatalogRepository(firestore.Db, f.Resources);
        await catalog.SaveItemAsync(f.Workspace.Id, Rc3Sprint4IntegrityTests.Fixture.Service(), true, default);
        var service = Rc3Sprint4IntegrityTests.Fixture.Service(); service.DurationInMinutes = 60;
        var document = firestore.Db.Collection("workspaces").Document(f.Workspace.Id.ToString()).Collection("catalogItems").Document("service");
        var snapshot = await document.GetSnapshotAsync();
        var stamp = await f.Resources.ReadAsync(new(f.Workspace.Id, ReservationResourceKind.Service, "service"), default);
        await f.Resources.BeginAsync(stamp, new("SaveService", service, false, true, null, snapshot.UpdateTime!.Value.ToDateTime()), default);
        var edit = Rc3Sprint4IntegrityTests.Fixture.Service(); edit.Name = "Later name";
        await catalog.SaveItemAsync(f.Workspace.Id, edit, false, default);
        using var provider = new ServiceCollection().AddSingleton<IReservationResourceCoordinator>(f.Resources)
            .AddSingleton(firestore.Db).AddSingleton(catalog).AddSingleton(new FirestoreLocationRepository(firestore.Db, f.Resources)).BuildServiceProvider();
        var worker = new ReservationResourceRecoveryWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ReservationResourceRecoveryWorker>.Instance);
        await worker.RecoverOnceAsync(default);
        Assert.Null((await f.Resources.ReadAsync(stamp.Key, default)).PendingOperationId);
        Assert.Equal("Later name", (await catalog.GetItemByIdAsync(f.Workspace.Id, "service", default))!.Name);
        Assert.Equal(30, ((NexFlow.Application.Features.Services.DTOs.ServiceDto)(await catalog.GetItemByIdAsync(f.Workspace.Id, "service", default))!).DurationInMinutes);
        await worker.RecoverOnceAsync(default); Assert.Null(f.Db.Database.CurrentTransaction);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public DateTime Now { get; set; } = new(2030, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        public Workspace Workspace { get; } = Workspace.Create("Workspace");
        public Mock<IClock> Clock { get; } = new(); public SqlAdapter Adapter { get; } = new();
        public BoundaryDb Db { get; private set; } = null!;
        public DbContextOptions<NexFlowDbContext> Options { get; private set; } = null!;
        public ReservationResourceCoordinator Resources { get; private set; } = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await f._connection.OpenAsync();
            f.Clock.SetupGet(c => c.UtcNow).Returns(() => f.Now);
            f.Options = new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(f._connection).AddInterceptors(f.Adapter).Options;
            f.Db = new(f.Options); await f.Db.Database.EnsureCreatedAsync();
            f.Db.Workspaces.Add(f.Workspace); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
            f.Resources = new(f.Db, f.Clock.Object); return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }
    private sealed class BoundaryDb(DbContextOptions<NexFlowDbContext> options) : NexFlowDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            foreach (var entity in model.Model.GetEntityTypes().ToArray()) model.Ignore(entity.ClrType);
            new ReservationConfiguration().Configure(model.Entity<Reservation>());
            model.Entity<Reservation>().Ignore(r => r.DomainEvents);
            model.Entity<Reservation>().Property(r => r.RowVersion).ValueGeneratedNever();
            new ReservationResourceStateConfiguration().Configure(model.Entity<ReservationResourceState>());
            model.Entity<Workspace>().HasKey(w => w.Id); model.Entity<Workspace>().Ignore(w => w.DomainEvents);
        }
    }
    private sealed class SqlAdapter : DbCommandInterceptor
    {
        public List<string> LockKinds { get; } = [];
        public int LifecycleCalls { get; private set; }
        private void Adapt(DbCommand command)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock_shared", StringComparison.Ordinal))
            { LifecycleCalls++; command.CommandText = "SELECT 1"; return; }
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
                LockKinds.Add(command.Parameters.Cast<DbParameter>().Select(p => p.Value?.ToString()).Single(v => v is "Location" or "Service")!);
            command.CommandText = command.CommandText.Replace("FOR UPDATE", "", StringComparison.Ordinal);
            command.CommandText = System.Text.RegularExpressions.Regex.Replace(command.CommandText, @"CAST\((@\w+) AS jsonb\)", "$1");
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Adapt(command); return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Adapt(command); return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }
    }
}
