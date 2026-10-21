using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using Xunit;

namespace NexFlow.Tests.Integration;

// Opt-in only through the existing disposable PostgreSQL/Firestore harness. Not run by Sprint 2 locally:
// fixture initialization applies historical migrations, which needs separate execution authorization.
[Trait("Category", "Integration")]
[Collection("RC3 isolated stores")]
public sealed class ReservationOperationIntegrationTests
{
    private const string Contact = "+51999999999";

    [IsolatedIntegrationFact]
    public async Task Committed_operation_replays_across_contexts_conflicts_on_payload_and_preserves_later_edits()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var seed = f.Db();
        var created = await f.Engine(seed).CreateReservationAsync(ws, "location", "service", Contact, "Synthetic", IsolatedStores.Day.AddHours(15), f.Ct, "original");
        Assert.True(created.IsSuccess);
        var candidate = (await new ReservationRepository(seed, f.Clock).GetOwnedFutureAsync(ws, Contact, created.Value.Id, f.Ct))!;
        var command = Command(ws, candidate, "confirm-1", 16);
        await using (var first = f.Db()) Assert.True((await f.Engine(first).ApplyExistingOperationAsync(command, f.Ct)).IsSuccess);
        // Discard the first result: recovery uses only the committed SQL receipt in another scope.
        await using (var replay = f.Db()) Assert.Equal(created.Value.Id, (await f.Engine(replay).ApplyExistingOperationAsync(command, f.Ct)).Value.Id);
        await using (var conflict = f.Db()) Assert.Equal("Reservation.IdempotencyConflict",
            (await f.Engine(conflict).ApplyExistingOperationAsync(command with { ProposedUtc = IsolatedStores.Day.AddHours(17) }, f.Ct)).Error.Code);
        await using var later = f.Db();
        var current = (await new ReservationRepository(later, f.Clock).GetOwnedFutureAsync(ws, Contact, created.Value.Id, f.Ct))!;
        Assert.True((await f.Engine(later).ApplyExistingOperationAsync(Command(ws, current, "confirm-2", 17), f.Ct)).IsSuccess);
        await using var oldReplay = f.Db(); Assert.True((await f.Engine(oldReplay).ApplyExistingOperationAsync(command, f.Ct)).IsSuccess);
        await using var check = f.Db(); var row = Assert.Single(await check.Reservations.ToListAsync(f.Ct));
        Assert.Equal(created.Value.Id, row.Id); Assert.Equal(IsolatedStores.Day.AddHours(17), row.StartTime);
        Assert.Equal("location", row.LocationId); Assert.Equal("service", row.ServiceId); Assert.Equal(ReservationStatus.Confirmed, row.Status);
        Assert.Equal(3, await check.OutboxMessages.CountAsync(f.Ct));
    }

    [IsolatedIntegrationFact]
    public async Task Concurrent_same_confirmation_has_one_mutation_and_one_operation_event()
    {
        await using var f = await IsolatedStores.Create(); var ws = await f.Workspace();
        await using var seed = f.Db();
        var created = await f.Engine(seed).CreateReservationAsync(ws, "location", "service", Contact, "Synthetic", IsolatedStores.Day.AddHours(15), f.Ct, "original");
        var candidate = (await new ReservationRepository(seed, f.Clock).GetOwnedFutureAsync(ws, Contact, created.Value.Id, f.Ct))!;
        var command = Command(ws, candidate, "same-confirmation", 16);
        await using var a = f.Db(); await using var b = f.Db();
        var results = await Task.WhenAll(f.Engine(a).ApplyExistingOperationAsync(command, f.Ct), f.Engine(b).ApplyExistingOperationAsync(command, f.Ct));
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error.Description));
        await using var check = f.Db(); Assert.Single(await check.Reservations.ToListAsync(f.Ct));
        Assert.Single(await check.OutboxMessages.Where(o => o.Id.StartsWith("resop_")).ToListAsync(f.Ct));
        Assert.Equal(IsolatedStores.Day.AddHours(16), (await check.Reservations.SingleAsync(f.Ct)).StartTime);
    }

    private static ReservationOperationCommand Command(Guid workspace, ReservationCandidate candidate, string source, int hour)
        => new(workspace, candidate.Id, Contact, source, ReservationOperationKind.RescheduleExistingReservation,
            candidate.RowVersion, candidate.StartTime, IsolatedStores.Day.AddHours(hour), ReservationOperationCommand.Hash("si confirmo"));
}
