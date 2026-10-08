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

    private static DateTime Utc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    private sealed class DbFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public Guid Workspace { get; } = Guid.NewGuid();
        public ReservationDb Db { get; private set; } = null!;
        public ReservationRepository Repository { get; private set; } = null!;
        public Mock<IClock> Clock { get; } = new(MockBehavior.Strict);
        public SqlCapture Sql { get; } = new();

        public static async Task<DbFixture> CreateAsync()
        {
            var fixture = new DbFixture(); await fixture._connection.OpenAsync();
            fixture.Db = new(new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(fixture._connection).AddInterceptors(fixture.Sql).Options);
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
