using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NexFlow.Domain.Entities;
using Npgsql;
using Xunit;

namespace NexFlow.Tests.Integration;

[Trait("Category", "Integration")]
[Collection("RC3 isolated stores")]
public sealed class ReservationMigrationIntegrationTests
{
    [IsolatedIntegrationFact]
    public async Task Up_preserves_rows_partial_index_reuses_cancelled_slots_and_source_is_still_unique()
    {
        await using var f = await IsolatedStores.Create(IsolatedStores.Previous); var ws = await f.Workspace(false);
        await using var db = f.Db(); var historical = Row(ws, "historical"); historical.Cancel();
        db.Reservations.Add(historical); await db.SaveChangesAsync(f.Ct);
        var oldXmin = historical.RowVersion;
        await db.GetService<IMigrator>().MigrateAsync(IsolatedStores.Target, f.Ct);
        Assert.False(db.Database.HasPendingModelChanges());
        await using var read = f.Db(); var preserved = await read.Reservations.SingleAsync(f.Ct);
        Assert.Equal(historical.Id, preserved.Id); Assert.Equal("historical", preserved.SourceMessageId); Assert.Equal(oldXmin, preserved.RowVersion);
        Assert.Empty(await read.Set<NexFlow.Domain.Entities.System.ReservationResourceState>().ToListAsync(f.Ct));
        var second = Row(ws, "second"); second.Cancel(); read.Reservations.Add(second); read.Reservations.Add(Row(ws, "active"));
        await read.SaveChangesAsync(f.Ct);
        await using (var duplicate = f.Db())
        {
            duplicate.Reservations.Add(Row(ws, "duplicate"));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(f.Ct));
            Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
        await using (var source = f.Db())
        {
            source.Reservations.Add(Row(ws, "historical", 60));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => source.SaveChangesAsync(f.Ct));
            var pg = Assert.IsType<PostgresException>(ex.InnerException); Assert.Equal("23505", pg.SqlState);
            Assert.Equal("IX_Reservations_WorkspaceId_SourceMessageId", pg.ConstraintName);
        }
        await using var check = f.Db(); Assert.Equal(3, await check.Reservations.CountAsync(f.Ct));
        Assert.Equal(2, await check.Reservations.CountAsync(r => r.Status == NexFlow.Domain.Enums.ReservationStatus.Cancelled, f.Ct));
        await using var connection = new NpgsqlConnection(f.ConnectionString); await connection.OpenAsync(f.Ct);
        await using var command = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_Reservations_WorkspaceId_LocationId_ServiceId_StartTime'", connection);
        var definition = Assert.IsType<string>(await command.ExecuteScalarAsync(f.Ct));
        Assert.Contains("UNIQUE", definition); Assert.Contains("WHERE", definition); Assert.Contains("Cancelled", definition);
    }

    [IsolatedIntegrationFact]
    public async Task Down_unused_database_restores_old_index_and_can_reapply_up()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(false);
        await using var db = f.Db(); var history = Row(ws, "safe"); db.Reservations.Add(history); await db.SaveChangesAsync(f.Ct);
        await db.GetService<IMigrator>().MigrateAsync(IsolatedStores.Previous, f.Ct);
        Assert.DoesNotContain(IsolatedStores.Target, await db.Database.GetAppliedMigrationsAsync(f.Ct));
        Assert.Equal(history.Id, (await db.Reservations.AsNoTracking().SingleAsync(f.Ct)).Id);
        await db.GetService<IMigrator>().MigrateAsync(IsolatedStores.Target, f.Ct);
        Assert.Contains(IsolatedStores.Target, await db.Database.GetAppliedMigrationsAsync(f.Ct));
        Assert.Equal(history.Id, (await db.Reservations.AsNoTracking().SingleAsync(f.Ct)).Id);
    }

    [IsolatedIntegrationFact]
    public async Task Down_refuses_even_ready_epochs_without_losing_history_or_migration_record()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(false);
        await using var db = f.Db(); await f.Coordinator(db).ReadAsync(f.Key(ws), f.Ct);
        var row = Row(ws, "preserve"); db.Reservations.Add(row); await db.SaveChangesAsync(f.Ct);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(IsolatedStores.Previous, f.Ct));
        Assert.Equal("55000", ex.SqlState);
        await using var check = f.Db(); Assert.Contains(IsolatedStores.Target, await check.Database.GetAppliedMigrationsAsync(f.Ct));
        Assert.Single(await check.Set<NexFlow.Domain.Entities.System.ReservationResourceState>().ToListAsync(f.Ct));
        Assert.Equal(row.Id, (await check.Reservations.SingleAsync(f.Ct)).Id);
    }

    [IsolatedIntegrationFact]
    public async Task Down_refuses_incompatible_cancelled_history_and_rolls_back_all_ddl()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace(false);
        await using var db = f.Db(); var old = Row(ws, "cancelled"); old.Cancel(); db.Reservations.Add(old); db.Reservations.Add(Row(ws, "current"));
        await db.SaveChangesAsync(f.Ct);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(IsolatedStores.Previous, f.Ct));
        Assert.Equal("55000", ex.SqlState);
        await using var check = f.Db(); Assert.Contains(IsolatedStores.Target, await check.Database.GetAppliedMigrationsAsync(f.Ct));
        Assert.Equal(2, await check.Reservations.CountAsync(f.Ct));
        Assert.Empty(await check.Set<NexFlow.Domain.Entities.System.ReservationResourceState>().ToListAsync(f.Ct));
        // The partial index survived the failed Down, not just the data.
        var another = Row(ws, "another-cancelled"); another.Cancel(); check.Reservations.Add(another); await check.SaveChangesAsync(f.Ct);
        Assert.Equal(3, await check.Reservations.CountAsync(f.Ct));
    }

    private static Reservation Row(Guid ws, string source, int minute = 0) => Reservation.Create(ws, "location", "service", "synthetic", "Synthetic",
        IsolatedStores.Day.AddHours(15).AddMinutes(minute), IsolatedStores.Day.AddHours(15).AddMinutes(minute + 30), source);
}
