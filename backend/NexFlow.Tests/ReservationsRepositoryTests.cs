using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using Xunit;

namespace NexFlow.Tests;

public sealed class ReservationsRepositoryTests
{
    [Fact]
    public async Task Historical_week_is_one_bounded_database_query_with_all_states_tenant_isolation_and_deterministic_order()
    {
        await using var f = await DbFixture.CreateAsync();
        var start = Utc("2018-07-02T05:00:00Z"); var end = start.AddDays(7);
        f.Add(f.Workspace, "location-a", "before", start.AddTicks(-1), ReservationStatus.Confirmed, 10);
        f.Add(f.Workspace, "location-a", "start", start, ReservationStatus.Pending, 3);
        f.Add(f.Workspace, "location-a", "later", start.AddHours(1), ReservationStatus.Confirmed, 4);
        f.Add(f.Workspace, "location-b", "tie-second", start.AddHours(2), ReservationStatus.Completed, 2);
        f.Add(f.Workspace, "deleted-location", "tie-first", start.AddHours(2), ReservationStatus.Cancelled, 1);
        f.Add(f.Workspace, "location-b", "tail", end.AddTicks(-1), ReservationStatus.Confirmed, 5);
        f.Add(f.Workspace, "location-a", "next-week", end, ReservationStatus.Completed, 6);
        f.Add(Guid.NewGuid(), "location-a", "foreign", start.AddHours(1), ReservationStatus.Confirmed, 7);
        await f.SeedAsync();

        var rows = (await f.Repository.GetReservationsForDateAsync(f.Workspace, null, start, end, default)).ToArray();

        Assert.Equal(new[] { Id(3), Id(4), Id(1), Id(2), Id(5) }, rows.Select(r => r.Id));
        Assert.All(rows, r => { Assert.Equal(f.Workspace, r.WorkspaceId); Assert.InRange(r.StartTime, start, end.AddTicks(-1)); });
        Assert.Equal(Enum.GetValues<ReservationStatus>().Order(), rows.Select(r => r.Status).Distinct().Order());
        Assert.Empty(f.Db.ChangeTracker.Entries());
        var sql = Assert.Single(f.Sql.Commands);
        Assert.Contains("\"WorkspaceId\" =", sql);
        Assert.Contains("\"StartTime\" >=", sql);
        Assert.Contains("\"StartTime\" <", sql);
        Assert.Contains("ORDER BY \"r\".\"StartTime\", \"r\".\"Id\"", sql);
        f.Clock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Adjacent_weeks_share_no_boundary_reservations_and_include_every_day()
    {
        await using var f = await DbFixture.CreateAsync();
        var monday = Utc("2026-12-28T05:00:00Z");
        for (var day = -1; day <= 7; day++) f.Add(f.Workspace, "location-a", "service", monday.AddDays(day), ReservationStatus.Confirmed, day + 20);
        await f.SeedAsync();

        var first = (await f.Repository.GetReservationsForDateAsync(f.Workspace, "location-a", monday, monday.AddDays(7), default)).ToArray();
        var next = (await f.Repository.GetReservationsForDateAsync(f.Workspace, "location-a", monday.AddDays(7), monday.AddDays(14), default)).ToArray();

        Assert.Equal(7, first.Length);
        Assert.Equal(Enumerable.Range(0, 7).Select(day => monday.AddDays(day)), first.Select(r => r.StartTime));
        Assert.Single(next); Assert.Equal(monday.AddDays(7), next[0].StartTime);
        Assert.Empty(first.Select(r => r.Id).Intersect(next.Select(r => r.Id)));
        Assert.Equal(2, f.Sql.Commands.Count); // One query for each complete week, never seven daily queries.
    }

    [Fact]
    public async Task Concrete_location_is_filtered_in_database_while_aggregate_includes_only_own_history()
    {
        await using var f = await DbFixture.CreateAsync();
        var start = Utc("2026-10-05T05:00:00Z");
        f.Add(f.Workspace, "location-a", "one", start, ReservationStatus.Confirmed, 1);
        f.Add(f.Workspace, "location-b", "two", start.AddHours(1), ReservationStatus.Completed, 2);
        f.Add(Guid.NewGuid(), "location-a", "foreign", start, ReservationStatus.Cancelled, 3);
        await f.SeedAsync();

        var selected = (await f.Repository.GetReservationsForDateAsync(f.Workspace, "location-a", start, start.AddDays(7), default)).ToArray();
        Assert.Equal(Id(1), Assert.Single(selected).Id);
        Assert.Contains("\"LocationId\" =", Assert.Single(f.Sql.Commands));
        f.Sql.Commands.Clear();
        var all = (await f.Repository.GetReservationsForDateAsync(f.Workspace, null, start, start.AddDays(7), default)).ToArray();
        Assert.Equal(new[] { Id(1), Id(2) }, all.Select(r => r.Id));
        Assert.Single(f.Sql.Commands);
    }

    [Fact]
    public async Task Reads_preserve_stored_statuses_and_do_not_change_availability_or_concurrency_metadata()
    {
        await using var f = await DbFixture.CreateAsync();
        var start = Utc("2026-10-05T05:00:00Z");
        f.Add(f.Workspace, "location-a", "busy", start, ReservationStatus.Confirmed, 1);
        f.Add(f.Workspace, "location-a", "cancelled", start.AddHours(1), ReservationStatus.Cancelled, 2);
        await f.SeedAsync();
        Assert.False(await f.Repository.IsTimeSlotAvailableAsync(f.Workspace, "location-a", start, start.AddMinutes(30)));
        Assert.True(await f.Repository.IsTimeSlotAvailableAsync(f.Workspace, "location-a", start.AddHours(1), start.AddHours(1.5)));

        var rows = (await f.Repository.GetReservationsForDateAsync(f.Workspace, null, start, start.AddDays(7), default)).ToArray();

        Assert.Equal(new[] { ReservationStatus.Confirmed, ReservationStatus.Cancelled }, rows.Select(r => r.Status));
        Assert.False(await f.Repository.IsTimeSlotAvailableAsync(f.Workspace, "location-a", start, start.AddMinutes(30)));
        Assert.True(await f.Repository.IsTimeSlotAvailableAsync(f.Workspace, "location-a", start.AddHours(1), start.AddHours(1.5)));
        var entity = f.Db.Model.FindEntityType(typeof(Reservation))!;
        Assert.True(entity.FindProperty(nameof(Reservation.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "WorkspaceId", "LocationId", "ServiceId", "StartTime" }));
    }

    [Fact]
    public async Task Database_query_respects_cancellation()
    {
        await using var f = await DbFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var start = Utc("2026-10-05T05:00:00Z");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Repository.GetReservationsForDateAsync(f.Workspace, null, start, start.AddDays(7), cancellation.Token));
    }

    [Fact]
    public async Task Future_service_and_location_guards_filter_status_time_and_workspace_in_sql()
    {
        await using var f = await DbFixture.CreateAsync();
        var now = Utc("2030-07-01T12:00:00Z"); f.Clock.SetupGet(c => c.UtcNow).Returns(now);
        f.Add(f.Workspace, "future", "service", now, ReservationStatus.Confirmed, 1);
        f.Add(f.Workspace, "past", "service", now.AddMinutes(-1), ReservationStatus.Confirmed, 2);
        f.Add(f.Workspace, "cancelled", "service", now.AddDays(1), ReservationStatus.Cancelled, 3);
        f.Add(f.Workspace, "completed", "service", now.AddDays(1), ReservationStatus.Completed, 4);
        f.Add(Guid.NewGuid(), "foreign", "service", now.AddDays(1), ReservationStatus.Confirmed, 5);
        f.Add(f.Workspace, "different", "other-service", now.AddDays(1), ReservationStatus.Confirmed, 6);
        await f.SeedAsync();
        Assert.Equal(new[] { "future" }, await f.Repository.GetFutureConfirmedServiceLocationsAsync(f.Workspace, "service", default));
        Assert.True(await f.Repository.HasFutureConfirmedAtLocationAsync(f.Workspace, "future", default));
        Assert.False(await f.Repository.HasFutureConfirmedAtLocationAsync(f.Workspace, "cancelled", default));
        Assert.False(await f.Repository.HasFutureConfirmedAtLocationAsync(f.Workspace, "foreign", default));
        Assert.Contains("\"ServiceId\" =", f.Sql.Commands[0]);
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Stale_reschedule_cannot_overwrite_concurrent_cancellation_using_mapped_xmin_predicate()
    {
        await using var f = await DbFixture.CreateAsync();
        var start = Utc("2030-07-01T12:00:00Z");
        f.Add(f.Workspace, "location", "service", start, ReservationStatus.Confirmed, 1); await f.SeedAsync();
        var stale = (await f.Repository.GetByIdAsync(f.Workspace, Id(1), default))!;
        // SQLite does not generate xmin. Simulate the database's next version in
        // a second context; this tests EF's concurrency predicate, not PostgreSQL locks.
        await using var winner = new ReservationDb(f.Options);
        var current = (await winner.Reservations.SingleAsync())!; current.Cancel();
        winner.Entry(current).Property(r => r.RowVersion).CurrentValue = 1;
        await winner.SaveChangesAsync();
        stale.Reschedule(start.AddHours(1), start.AddHours(1.5));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        var persisted = (await f.Repository.GetByIdAsync(f.Workspace, Id(1), default))!;
        Assert.Equal(ReservationStatus.Cancelled, persisted.Status); Assert.Equal(start, persisted.StartTime);
        Assert.Equal("service", persisted.ServiceId); Assert.Equal("location", persisted.LocationId);
    }

    [Fact]
    public async Task Cancelled_history_does_not_block_rebooking_but_active_rows_remain_unique()
    {
        await using var f = await DbFixture.CreateAsync(); var start = Utc("2030-07-01T12:00:00Z");
        f.Add(f.Workspace, "location", "service", start, ReservationStatus.Cancelled, 1); await f.SeedAsync();
        Assert.True(await f.Repository.IsTimeSlotAvailableAsync(f.Workspace, "location", start, start.AddMinutes(30)));
        f.Repository.Add(Reservation.Create(f.Workspace, "location", "service", "other", "Other", start, start.AddMinutes(30)));
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var active = (await f.Db.Reservations.SingleAsync(r => r.Status == ReservationStatus.Confirmed)); active.Cancel();
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        f.Repository.Add(Reservation.Create(f.Workspace, "location", "service", "third", "Third", start, start.AddMinutes(30)));
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        Assert.Equal(2, await f.Db.Reservations.CountAsync(r => r.Status == ReservationStatus.Cancelled));
        Assert.Single(await f.Db.Reservations.Where(r => r.Status == ReservationStatus.Confirmed).ToListAsync());
        f.Repository.Add(Reservation.Create(f.Workspace, "location", "service", "fourth", "Fourth", start, start.AddMinutes(30)));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)] [InlineData(ReservationStatus.Pending)] [InlineData(ReservationStatus.Completed)]
    public async Task Every_non_cancelled_status_has_matching_availability_and_unique_index_rules(ReservationStatus status)
    {
        await using var f = await DbFixture.CreateAsync(); var start = Utc("2030-07-01T12:00:00Z");
        f.Add(f.Workspace, "location", "service", start, status, 1); await f.SeedAsync();
        Assert.False(await f.Repository.IsTimeSlotAvailableAsync(f.Workspace, "location", start, start.AddMinutes(30)));
        f.Repository.Add(Reservation.Create(f.Workspace, "location", "service", "other", "Other", start, start.AddMinutes(30)));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Cancellation_does_not_release_the_independent_source_message_identity()
    {
        await using var f = await DbFixture.CreateAsync(); var start = Utc("2030-07-01T12:00:00Z");
        var original = Reservation.Create(f.Workspace, "location", "service", "customer", "Customer", start, start.AddMinutes(30), "source");
        original.Cancel(); f.Repository.Add(original); await f.SeedAsync();
        Assert.Equal(original.Id, (await f.Repository.GetBySourceMessageIdAsync(f.Workspace, "source", default))!.Id);
        f.Repository.Add(Reservation.Create(f.Workspace, "other-location", "other-service", "customer", "Customer", start.AddHours(1), start.AddHours(1.5), "source"));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.Contains("SourceMessageId", error.InnerException!.Message);
    }

    [Fact]
    public async Task Rescheduling_into_another_active_exact_slot_is_rejected_without_rewriting_either_reservation()
    {
        await using var f = await DbFixture.CreateAsync(); var start = Utc("2030-07-01T12:00:00Z");
        f.Add(f.Workspace, "location", "service", start, ReservationStatus.Confirmed, 1);
        f.Add(f.Workspace, "location", "service", start.AddHours(1), ReservationStatus.Confirmed, 2); await f.SeedAsync();
        var row = (await f.Repository.GetByIdAsync(f.Workspace, Id(2), default))!;
        row.Reschedule(start, start.AddMinutes(30));
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync()); f.Db.ChangeTracker.Clear();
        Assert.Equal(start, (await f.Repository.GetByIdAsync(f.Workspace, Id(1), default))!.StartTime);
        Assert.Equal(start.AddHours(1), (await f.Repository.GetByIdAsync(f.Workspace, Id(2), default))!.StartTime);
    }

    private static DateTime Utc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    private sealed class DbFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public Guid Workspace { get; } = Guid.NewGuid();
        public ReservationDb Db { get; private set; } = null!;
        public DbContextOptions<NexFlowDbContext> Options { get; private set; } = null!;
        public ReservationRepository Repository { get; private set; } = null!;
        public Mock<IClock> Clock { get; } = new(MockBehavior.Strict);
        public SqlCapture Sql { get; } = new();

        public static async Task<DbFixture> CreateAsync()
        {
            var fixture = new DbFixture(); await fixture._connection.OpenAsync();
            fixture.Options = new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(fixture._connection).AddInterceptors(fixture.Sql).Options;
            fixture.Db = new(fixture.Options);
            await fixture.Db.Database.EnsureCreatedAsync();
            fixture.Repository = new(fixture.Db, fixture.Clock.Object);
            fixture.Sql.Commands.Clear();
            return fixture;
        }

        public void Add(Guid workspace, string location, string service, DateTime start, ReservationStatus state, int id)
        {
            var row = Reservation.Create(workspace, location, service, "customer", "Customer", start, start.AddMinutes(30));
            // Seed historical rows with their existing identifiers/states rather than exercising mutation workflows.
            Db.Entry(row).Property(r => r.Id).CurrentValue = Id(id);
            Db.Entry(row).Property(r => r.Status).CurrentValue = state;
            Db.Reservations.Add(row);
        }

        public async Task SeedAsync() { await Db.SaveChangesAsync(); Db.ChangeTracker.Clear(); Sql.Commands.Clear(); }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class ReservationDb(DbContextOptions<NexFlowDbContext> options) : NexFlowDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToArray()) modelBuilder.Ignore(entity.ClrType);
            new ReservationConfiguration().Configure(modelBuilder.Entity<Reservation>());
            modelBuilder.Entity<Reservation>().Ignore(r => r.DomainEvents);
            // SQLite has no PostgreSQL xmin generation. Keep its concurrency flag and leave the production mapping untouched.
            modelBuilder.Entity<Reservation>().Property(r => r.RowVersion).ValueGeneratedNever();
        }
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
