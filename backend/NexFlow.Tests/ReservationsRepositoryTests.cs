using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Domain.Entities.System;
using System.Text.Json;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using Xunit;

namespace NexFlow.Tests;

public sealed class ReservationsRepositoryTests
{
    [Theory]
    [InlineData("987654321", null, null)]
    [InlineData("987654321", "PE", "+51987654321")]
    [InlineData("987654321", "US", null)]
    [InlineData("51987654321", null, "+51987654321")]
    [InlineData("+51 987-654-321", null, "+51987654321")]
    [InlineData("+987654321", "PE", null)]
    [InlineData("+051987654321", null, null)]
    [InlineData("51987654321@untrusted", null, null)]
    public void Contact_normalization_requires_an_explicit_national_country(string input, string? country, string? expected)
    {
        Assert.Equal(expected != null, ReservationContact.TryNormalize(input, country, out var actual));
        Assert.Equal(expected ?? string.Empty, actual);
    }

    [Fact]
    public void Historical_aliases_are_exact_international_forms_and_do_not_guess_national_identity()
    {
        const string canonical = "+51987654321";
        Assert.Equal(new[] { canonical, "51987654321" }, ReservationContact.InternationalAliases(canonical));
        Assert.True(ReservationContact.IsSameInternationalContact("51987654321", canonical));
        Assert.False(ReservationContact.IsSameInternationalContact("987654321", canonical));
        Assert.False(ReservationContact.IsSameInternationalContact("+52987654321", canonical));
        Assert.Equal(canonical, NexFlow.Application.Features.Automation.ProcessMessage.Services.IncomingMessageGuard.NormalizePhone("51987654321@s.whatsapp.net"));
    }

    [Fact]
    public async Task Future_contact_page_is_bounded_in_sql_and_details_recheck_owner_workspace_and_state()
    {
        await using var f = await DbFixture.CreateAsync(); var now = Utc("2030-07-01T12:00:00Z");
        f.Clock.SetupGet(c => c.UtcNow).Returns(now);
        const string contact = "+51999999999";
        for (var i = 1; i <= 9; i++)
        {
            var row = Reservation.Create(i == 9 ? Guid.NewGuid() : f.Workspace, $"location-{i}", "service", i == 8 ? "+51888888888" : contact,
                "Customer", now.AddHours(i <= 5 ? 1 : i), now.AddHours(i <= 5 ? 1 : i).AddMinutes(30));
            f.Db.Entry(row).Property(r => r.Id).CurrentValue = Id(i);
            if (i == 7) row.Cancel(); f.Repository.Add(row);
        }
        await f.SeedAsync();
        var page = await f.Repository.GetFutureForContactAsync(f.Workspace, contact, null, 5, default);
        Assert.Equal(Enumerable.Range(1, 5).Select(Id), page.Items.Select(r => r.Id)); Assert.True(page.HasMore);
        var sql = Assert.Single(f.Sql.Commands); Assert.Contains("LIMIT", sql); Assert.Contains("ORDER BY", sql);
        Assert.Contains("CustomerIdentifier", sql); Assert.Contains("Status", sql); Assert.DoesNotContain("CustomerName", sql);
        f.Sql.Commands.Clear(); var last = page.Items[^1];
        var second = await f.Repository.GetFutureForContactAsync(f.Workspace, contact, new(DateTime.SpecifyKind(last.StartTime, DateTimeKind.Utc), last.Id), 5, default);
        Assert.Equal(Id(6), Assert.Single(second.Items).Id); Assert.False(second.HasMore);
        Assert.Null(await f.Repository.GetOwnedFutureAsync(Guid.NewGuid(), contact, Id(1), default));
        Assert.Null(await f.Repository.GetOwnedFutureAsync(f.Workspace, "+51888888888", Id(1), default));
        Assert.Null(await f.Repository.GetOwnedFutureAsync(f.Workspace, contact, Id(7), default));
        Assert.NotNull(await f.Repository.GetOwnedFutureAsync(f.Workspace, contact, Id(1), default));
        f.Clock.SetupGet(c => c.UtcNow).Returns(now.AddDays(2));
        Assert.Null(await f.Repository.GetOwnedFutureAsync(f.Workspace, contact, Id(1), default));
    }

    [Fact]
    public async Task Contact_owner_check_is_bounded_and_does_not_assume_a_shared_number_is_one_person()
    {
        await using var f = await DbFixture.CreateAsync(); var now = Utc("2030-07-01T12:00:00Z");
        f.Clock.SetupGet(c => c.UtcNow).Returns(now); const string phone = "+51999999999";
        f.Repository.Add(Reservation.Create(f.Workspace, "l", "s", phone, "Ana", now.AddHours(1), now.AddHours(1.5)));
        await f.SeedAsync(); Assert.True(await f.Repository.HasUnambiguousFutureOwnerNameAsync(f.Workspace, phone, default));
        f.Repository.Add(Reservation.Create(f.Workspace, "l", "s", phone, "Otra persona", now.AddHours(2), now.AddHours(2.5)));
        await f.SeedAsync(); Assert.False(await f.Repository.HasUnambiguousFutureOwnerNameAsync(f.Workspace, phone, default));
        Assert.Contains("DISTINCT", Assert.Single(f.Sql.Commands)); Assert.Contains("LIMIT", Assert.Single(f.Sql.Commands));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Repository.GetFutureForContactAsync(Guid.Empty, phone, null, 5, default));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Repository.GetFutureForContactAsync(f.Workspace, "unverified", null, 5, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Repository.GetFutureForContactAsync(f.Workspace, phone, null, 6, default));
    }

    [Fact]
    public void Npgsql_translates_the_actual_keyset_projection_without_opening_a_connection()
    {
        using var db = new NexFlowDbContext(new DbContextOptionsBuilder<NexFlowDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=not-opened;Username=synthetic").Options);
        var clock = Mock.Of<IClock>(c => c.UtcNow == new DateTime(2030, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var sql = new ReservationRepository(db, clock).QueryFutureForContact(Guid.NewGuid(), "+51999999999",
            new(new DateTime(2030, 7, 2, 0, 0, 0, DateTimeKind.Utc), Guid.NewGuid()), 5).ToQueryString();
        Assert.Contains("LIMIT", sql); Assert.Contains("ORDER BY", sql); Assert.Contains("xmin", sql);
        Assert.Contains("ANY", sql);
        Assert.Contains("CustomerIdentifier", sql); Assert.DoesNotContain("CustomerName", sql);
    }

    [Fact]
    public async Task Existing_outbox_primary_key_rolls_back_duplicate_receipt_and_reservation_change_atomically()
    {
        await using var f = await DbFixture.CreateAsync(); const string contact = "+51999999999";
        var start = Utc("2030-07-02T15:00:00Z");
        var row = Reservation.Create(f.Workspace, "location", "service", contact, "Ana", start, start.AddMinutes(30));
        f.Repository.Add(row); await f.SeedAsync();
        var operation = new ReservationOperationCommand(f.Workspace, row.Id, contact, "confirmation", ReservationOperationKind.RescheduleExistingReservation,
            0, start, start.AddHours(1), ReservationOperationCommand.Hash("si confirmo"));
        var result = new ReservationDto(row.Id, f.Workspace, row.LocationId, row.ServiceId, row.CustomerName, contact, start.AddHours(1), "Confirmed");
        var message = ReservationOperationReceipt.Create(operation, "RESERVATION_RESCHEDULED", result, result, start);
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        {
            var tracked = (await f.Repository.GetByIdAsync(f.Workspace, row.Id, default))!;
            tracked.Reschedule(start.AddHours(1), start.AddHours(1.5));
            await new PostgresOutboxRepository(f.Db).AddAsync(message, default);
            await f.Db.SaveChangesAsync(); await tx.CommitAsync();
        }
        f.Db.ChangeTracker.Clear();
        var stored = (await new PostgresOutboxRepository(f.Db).GetByIdAsync(f.Workspace, operation.Key, default))!;
        Assert.Equal(operation.Fingerprint, ReservationOperationReceipt.Read(stored, f.Workspace, contact).Fingerprint);
        var forwarded = JsonSerializer.Deserialize<N8nEventPayload<JsonElement>>(stored.PayloadJson)!;
        Assert.Equal(operation.Key, forwarded.IdempotencyKey);
        Assert.DoesNotContain("ReservationOperationReceipt", JsonSerializer.Serialize(forwarded));
        await using (var tx = await f.Db.Database.BeginTransactionAsync())
        {
            var tracked = (await f.Repository.GetByIdAsync(f.Workspace, row.Id, default))!;
            tracked.Reschedule(start.AddHours(2), start.AddHours(2.5));
            await new PostgresOutboxRepository(f.Db).AddAsync(ReservationOperationReceipt.Create(operation, "RESERVATION_RESCHEDULED", result, result, start), default);
            await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync()); await tx.RollbackAsync();
        }
        f.Db.ChangeTracker.Clear();
        Assert.Equal(start.AddHours(1), (await f.Repository.GetByIdAsync(f.Workspace, row.Id, default))!.StartTime);
        Assert.Equal(row.Id, (await f.Repository.GetByIdAsync(f.Workspace, row.Id, default))!.Id);
        Assert.Single(await f.Db.OutboxMessages.ToListAsync());
    }
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
            modelBuilder.Entity<OutboxMessage>().HasKey(r => r.Id);
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
