using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.Reservation;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Business.LocationAvailability;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class ExistingReservationSprint2Tests
{
    [Theory]
    [InlineData("quiero editar mi reserva", true, ReservationOperationKind.EditActiveDraft)]
    [InlineData("quiero editar mi reserva", false, ReservationOperationKind.RescheduleExistingReservation)]
    [InlineData("quiero editar mi reserva confirmada", true, ReservationOperationKind.RescheduleExistingReservation)]
    [InlineData("cancelar reserva", true, ReservationOperationKind.EditActiveDraft)]
    [InlineData("cancelar mi cita", false, ReservationOperationKind.CancelExistingReservation)]
    [InlineData("quiero ver mis citas", false, ReservationOperationKind.ViewExistingReservation)]
    [InlineData("quiero crear una nueva reserva y cambiar mi cita", false, ReservationOperationKind.AmbiguousReservationOperation)]
    public void Scope_is_a_typed_operation_not_a_creation_intent(string text, bool draft, ReservationOperationKind expected)
        => Assert.Equal(expected, ReservationOperationClassifier.Classify(text, draft));

    [Fact]
    public async Task Single_future_appointment_requires_handoff_before_any_private_read_or_mutation()
    {
        var f = new Fixture(); var row = f.Add(); var start = row.StartTime;
        await f.Turn("quiero editar mi reserva", "start"); await f.Turn("quiero editar mi reserva", "start");
        Assert.Contains("verificar tu titularidad", f.Text("start")); Assert.DoesNotContain("23/10", f.Text("start"));
        Assert.DoesNotContain("Sede Concepcion", f.Text("start")); Assert.DoesNotContain("Ana Pérez", f.Text("start"));
        Assert.Equal(1, f.Dialog.Handoffs); Assert.Equal("Human", (await f.Dialog.Context()).Mode);
        Assert.Equal(start, row.StartTime); Assert.Single(f.Rows); Assert.Empty(f.Events); Assert.Equal(0, f.Saves);
        Assert.Equal(0, f.PrivateReads); Assert.Equal(0, f.Dialog.AiCalls); Assert.Empty(f.Dialog.Bookings);
    }

    [Fact]
    public async Task Several_appointments_are_not_listed_without_independent_owner_authorization()
    {
        var f = new Fixture(); for (var i = 0; i < 7; i++) f.Add(day: 23 + i);
        await f.Turn("quiero ver mis citas", "view");
        Assert.Equal(1, f.Dialog.Handoffs); Assert.Equal(0, f.PrivateReads);
        Assert.DoesNotContain("23/10", f.Text("view")); Assert.Null((await f.Dialog.Context()).ReservationManagement);
        Assert.Empty(f.Events); Assert.Equal(7, f.Rows.Count);
    }

    [Fact]
    public async Task Zero_foreign_and_shared_contacts_all_handoff_without_disclosing_whether_a_booking_exists()
    {
        foreach (var state in new[] { "zero", "foreign", "shared", "same-name" })
        {
            var f = new Fixture();
            if (state == "foreign") { f.Add(workspace: Guid.NewGuid()); f.Add(contact: "+51888888888"); }
            if (state is "shared" or "same-name") { f.Add(); f.Add(day: 24, name: state == "shared" ? "Otra persona" : "Ana Pérez"); }
            await f.Turn("quiero ver mis citas", "view");
            Assert.Contains("verificar tu titularidad", f.Text("view")); Assert.Equal(1, f.Dialog.Handoffs);
            Assert.Equal(0, f.PrivateReads); Assert.Empty(f.Events); Assert.Equal(0, f.Saves);
        }
    }

    [Fact]
    public async Task Cancellation_and_later_confirmation_cannot_mutate_during_human_verification()
    {
        var f = new Fixture(); var row = f.Add();
        await f.Turn("quiero cancelar mi cita", "cancel"); await f.Turn("sí confirmo", "confirm");
        Assert.Equal(ReservationStatus.Confirmed, row.Status); Assert.Empty(f.Events); Assert.Equal(0, f.Saves);
        Assert.Equal(1, f.Dialog.Handoffs); Assert.False(f.Dialog.Sent.ContainsKey("confirm"));
    }

    [Fact]
    public async Task Public_information_and_existing_draft_edit_remain_available_before_handoff()
    {
        var f = new Fixture(); f.Add(); await f.Dialog.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Dialog.Context());
        await f.Turn("cuáles son sus horarios", "hours");
        Assert.Contains("Dato real autorizado", f.Text("hours"));
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Dialog.Context()));
        await f.Turn("quiero editar mi reserva", "draft");
        Assert.Equal(0, f.Dialog.Handoffs); Assert.Null((await f.Dialog.Context()).ReservationManagement);
        Assert.Equal(0, f.PrivateReads); Assert.Empty(f.Events);
        await f.Turn("quiero editar mi reserva confirmada", "registered");
        Assert.Equal(1, f.Dialog.Handoffs); Assert.Equal(0, f.PrivateReads); Assert.Empty(f.Events);
    }

    [Fact]
    public async Task Committed_engine_operation_replays_conflicts_on_changed_payload_and_allows_a_later_edit()
    {
        var f = new Fixture(); var row = f.Add(); var command = Command(f, row, "confirm", 21);
        var committed = await f.RealEngine!.ApplyExistingOperationAsync(command, default); Assert.True(committed.IsSuccess);
        var replay = await f.RealEngine.ApplyExistingOperationAsync(command, default);
        Assert.True(replay.IsSuccess); Assert.Equal(row.Id, replay.Value.Id); Assert.Single(f.Events); Assert.Equal(1, f.Saves);
        var conflict = await f.RealEngine.ApplyExistingOperationAsync(command with { ProposedUtc = command.ProposedUtc!.Value.AddHours(1) }, default);
        Assert.Equal("Reservation.IdempotencyConflict", conflict.Error.Code); Assert.Single(f.Events);
        var next = await f.RealEngine.ApplyExistingOperationAsync(Command(f, row, "new-confirm", 15), default);
        Assert.True(next.IsSuccess); Assert.Equal(row.Id, next.Value.Id); Assert.Equal(2, f.Events.Count); Assert.Equal(2, f.Saves);
    }

    [Fact]
    public async Task Old_committed_source_does_not_change_a_later_reservation_or_requeue_its_notification()
    {
        var f = new Fixture(); var row = f.Add(); var old = Command(f, row, "old", 21);
        Assert.True((await f.RealEngine!.ApplyExistingOperationAsync(old, default)).IsSuccess);
        Assert.True((await f.RealEngine.ApplyExistingOperationAsync(Command(f, row, "new", 15), default)).IsSuccess);
        var current = row.StartTime;
        Assert.True((await f.RealEngine.ApplyExistingOperationAsync(old, default)).IsSuccess);
        Assert.Equal(current, row.StartTime); Assert.Equal(2, f.Events.Count); Assert.Equal(2, f.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_committed_between_initial_lookup_and_selection_validation_is_replayed(bool cancel)
    {
        var f = new Fixture(); var row = f.Add();
        var command = Command(f, row, "confirmed", 21) with { Action = cancel ? ReservationOperationKind.CancelExistingReservation : ReservationOperationKind.RescheduleExistingReservation, ProposedUtc = cancel ? null : new DateTime(2026, 10, 23, 21, 0, 0, DateTimeKind.Utc) };
        Assert.True((await f.RealEngine!.ApplyExistingOperationAsync(command, default)).IsSuccess);
        f.MissFirstReceiptRead = true; var result = await f.RealEngine.ApplyExistingOperationAsync(command, default);
        Assert.True(result.IsSuccess); Assert.Equal(row.Id, result.Value.Id);
        Assert.Equal(cancel ? "Cancelled" : "Confirmed", result.Value.Status);
        Assert.Single(f.Events); Assert.Equal(1, f.Saves); Assert.False(f.MissFirstReceiptRead);
    }

    [Fact]
    public async Task Legacy_pending_management_cannot_confirm_or_disclose_private_information()
    {
        var f = new Fixture(); var row = f.Add(); var context = await f.Dialog.Context();
        context.CurrentGoal = "RESERVATION_MANAGEMENT";
        context.ReservationManagement = new() { WorkspaceId = f.Dialog.Workspace, ContactHash = ReservationOperationCommand.Hash(AssistantConversationTests.Fixture.Phone),
            Operation = ReservationOperationKind.RescheduleExistingReservation, Stage = "Confirm", Selected = new(row.Id, row.LocationId, row.ServiceId, row.StartTime, row.EndTime, row.RowVersion),
            ExpiresAt = f.Dialog.Now.AddMinutes(10), NextQuestion = "PRIVATE OLD SUMMARY" };
        await f.Dialog.Contexts.SaveContextAsync(f.Dialog.Workspace, AssistantConversationTests.Fixture.Phone, context, default);
        await f.Turn("sí confirmo", "old-context");
        Assert.Equal(1, f.Dialog.Handoffs); Assert.Equal(0, f.PrivateReads); Assert.Empty(f.Events);
        Assert.DoesNotContain("PRIVATE", f.Text("old-context")); Assert.Equal(15, row.StartTime.Hour);

        var publicTurn = new Fixture(); var saved = await publicTurn.Dialog.Context();
        saved.CurrentGoal = "RESERVATION_MANAGEMENT";
        saved.ReservationManagement = context.ReservationManagement;
        await publicTurn.Dialog.Contexts.SaveContextAsync(publicTurn.Dialog.Workspace, AssistantConversationTests.Fixture.Phone, saved, default);
        await publicTurn.Turn("Antes dime qué servicios tienen", "public");
        Assert.DoesNotContain("PRIVATE OLD SUMMARY", publicTurn.Text("public"));
        Assert.Equal(0, publicTurn.Dialog.Handoffs); Assert.Equal(0, publicTurn.PrivateReads);
        Assert.Null((await publicTurn.Dialog.Context()).ReservationManagement); Assert.Empty(publicTurn.Events);
    }

    [Fact]
    public async Task Revoked_module_is_denied_before_private_reads_or_a_reservation_handoff()
    {
        var f = new Fixture(); f.Add(); f.Dialog.Modules.Remove("RESERVATIONS");
        await f.Turn("quiero consultar mi cita", "denied");
        Assert.Contains("no tiene habilitadas las reservas", f.Text("denied"));
        Assert.Equal(0, f.Dialog.Handoffs); Assert.Equal(0, f.PrivateReads); Assert.Empty(f.Events);
    }

    [Fact]
    public async Task Slot_conflict_in_retained_engine_preserves_the_original_reservation_and_event_count()
    {
        var f = new Fixture(); var row = f.Add(); f.Add(contact: "+51888888888", hour: 21);
        var result = await f.RealEngine!.ApplyExistingOperationAsync(Command(f, row, "confirm", 21), default);
        Assert.Equal("Reservation.Conflict", result.Error.Code); Assert.Empty(f.Events); Assert.Equal(0, f.Saves); Assert.Equal(15, row.StartTime.Hour);
    }

    [Fact]
    public async Task Reschedule_availability_excludes_only_the_owned_appointment_not_other_contacts()
    {
        var f = new Fixture(); var row = f.Add(durationMinutes: 60); f.Add(contact: "+51888888888", hour: 16);
        var date = new DateTime(2026, 10, 23);
        var normal = await f.RealEngine!.GetAvailabilityAsync(f.Dialog.Workspace, "sede-a", "service-a", date, default);
        var editing = await f.RealEngine.GetAvailabilityForExistingAsync(f.Dialog.Workspace, AssistantConversationTests.Fixture.Phone, row.Id, date, default);
        var overlappingOwn = new DateTime(2026, 10, 23, 15, 30, 0, DateTimeKind.Utc);
        Assert.DoesNotContain(normal, s => s.StartTime == overlappingOwn); Assert.Contains(editing, s => s.StartTime == overlappingOwn);
        Assert.DoesNotContain(editing, s => s.StartTime == overlappingOwn.AddMinutes(30));
        Assert.Empty(await f.RealEngine.GetAvailabilityForExistingAsync(f.Dialog.Workspace, AssistantConversationTests.Fixture.Phone, f.Rows.Values.Single(r => r.Id != row.Id).Id, date, default));
        Assert.Empty(f.Events); Assert.Equal(0, f.Saves);
    }

    private static ReservationOperationCommand Command(Fixture f, Reservation row, string source, int hour)
        => new(f.Dialog.Workspace, row.Id, AssistantConversationTests.Fixture.Phone, source, ReservationOperationKind.RescheduleExistingReservation,
            row.RowVersion, row.StartTime, new DateTime(2026, 10, 23, hour, 0, 0, DateTimeKind.Utc), ReservationOperationCommand.Hash("si confirmo"));

    private sealed class Fixture
    {
        internal AssistantConversationTests.Fixture Dialog { get; }
        internal Dictionary<Guid, Reservation> Rows { get; } = [];
        internal Dictionary<string, OutboxMessage> Events { get; } = [];
        internal int Saves { get; private set; }
        internal int PrivateReads { get; private set; }
        internal bool MissFirstReceiptRead { get; set; }
        internal ReservationEngine? RealEngine { get; private set; }
        private readonly Dictionary<Guid, (DateTime Start, ReservationStatus Status)> previous = [];

        internal Fixture() => Dialog = new(conciergeCatalog: true, managementFactory: Build)
            { Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc) };
        internal Reservation Add(int day = 23, string contact = AssistantConversationTests.Fixture.Phone, string name = "Ana Pérez", Guid? workspace = null, int hour = 15, int durationMinutes = 30)
        {
            var start = new DateTime(2026, 10, day, hour, 0, 0, DateTimeKind.Utc);
            var row = Reservation.Create(workspace ?? Dialog.Workspace, "sede-a", "service-a", contact, name, start, start.AddMinutes(durationMinutes));
            Rows.Add(row.Id, row); previous[row.Id] = (row.StartTime, row.Status); return row;
        }
        internal Task Turn(string text, string source) => Dialog.HandleTurn(text, source);
        internal string Text(string source) => Dialog.Sent[source].Content;

        private IExistingReservationFlow Build(AssistantConversationTests.Fixture f)
        {
            var repo = new Mock<IReservationRepository>(MockBehavior.Strict); var outbox = new Mock<IOutboxRepository>(MockBehavior.Strict);
            IEnumerable<Reservation> Future(Guid w, string p) => Rows.Values.Where(r => r.WorkspaceId == w && r.CustomerIdentifier == p && r.Status == ReservationStatus.Confirmed && r.StartTime > f.Now);
            ReservationCandidate Capture(Reservation r) => new(r.Id, r.LocationId, r.ServiceId, r.StartTime, r.EndTime, r.RowVersion);
            repo.Setup(r => r.GetFutureForContactAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<ReservationCursor?>(), 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid w, string p, ReservationCursor? after, int size, CancellationToken _) => {
                    PrivateReads++; var rows = Future(w, p).Where(r => after == null || r.StartTime > after.StartTime || r.StartTime == after.StartTime && r.Id.CompareTo(after.Id) > 0).OrderBy(r => r.StartTime).ThenBy(r => r.Id).Take(6).ToArray();
                    return new ReservationPage(rows.Take(5).Select(Capture).ToArray(), rows.Length > 5);
                });
            repo.Setup(r => r.GetOwnedFutureAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid w, string p, Guid id, CancellationToken _) => { PrivateReads++; return Future(w, p).Where(r => r.Id == id).Select(Capture).SingleOrDefault(); });
            repo.Setup(r => r.HasUnambiguousFutureOwnerNameAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Future(f.Workspace, AssistantConversationTests.Fixture.Phone).Select(r => r.CustomerName).Distinct().Count() == 1);
            repo.Setup(r => r.GetByIdAsync(f.Workspace, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid _, Guid id, CancellationToken _) => Rows.GetValueOrDefault(id));
            repo.Setup(r => r.GetReservationsForDateAsync(f.Workspace, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid w, string l, DateTime from, DateTime to, CancellationToken _) => Rows.Values.Where(r => r.WorkspaceId == w && r.LocationId == l && r.StartTime >= from && r.StartTime < to).ToArray());
            repo.Setup(r => r.IsTimeSlotAvailableAsync(f.Workspace, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid w, string l, DateTime start, DateTime end, Guid? exclude, CancellationToken _) => !Rows.Values.Any(r => r.WorkspaceId == w && r.LocationId == l && r.Id != exclude && r.Status != ReservationStatus.Cancelled && r.StartTime < end && r.EndTime > start));
            outbox.Setup(o => o.GetByIdAsync(f.Workspace, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid _, string key, CancellationToken _) => {
                if (MissFirstReceiptRead) { MissFirstReceiptRead = false; return null; }
                return Events.GetValueOrDefault(key);
            });
            outbox.Setup(o => o.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).Callback((OutboxMessage message, CancellationToken _) => Events.Add(message.Id, message)).Returns(Task.CompletedTask);
            var dynamicClock = new Mock<IClock>(); dynamicClock.SetupGet(c => c.UtcNow).Returns(() => f.Now);
            var catalog = new Mock<ICatalogRepository>(MockBehavior.Strict);
            catalog.Setup(c => c.GetItemByIdAsync(f.Workspace, "service-a", It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Services[0]);
            var available = new Mock<ILocationAvailabilityService>(); available.Setup(a => a.IsOfferingAvailableAtLocation(It.IsAny<BusinessOfferingDto>(), "sede-a")).Returns(true);
            var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
            uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => {
                Saves++; foreach (var row in Rows.Values.Where(r => previous[r.Id] != (r.StartTime, r.Status))) {
                    typeof(Reservation).GetProperty(nameof(Reservation.RowVersion))!.SetValue(row, row.RowVersion + 1); previous[row.Id] = (row.StartTime, row.Status);
                } return 1;
            });
            uow.Setup(u => u.DiscardChanges(It.IsAny<object[]>()));
            RealEngine = new(repo.Object, catalog.Object, f.HoursReads.Object, f.ProfileReads.Object, f.LocationReads.Object,
                uow.Object, available.Object, dynamicClock.Object, outbox.Object, new Fakes.InMemoryReservationResourceCoordinator());
            f.EngineReads.Setup(e => e.ApplyExistingOperationAsync(It.IsAny<ReservationOperationCommand>(), It.IsAny<CancellationToken>()))
                .Returns(async (ReservationOperationCommand command, CancellationToken ct) => {
                    return await RealEngine.ApplyExistingOperationAsync(command, ct);
                });
            f.EngineReads.Setup(e => e.GetAvailabilityForExistingAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns((Guid w, string _, Guid id, DateTime date, CancellationToken ct) => f.EngineReads.Object.GetAvailabilityAsync(w, Rows[id].LocationId, Rows[id].ServiceId, date, ct));
            return new ExistingReservationFlow(repo.Object, f.EngineReads.Object, outbox.Object, f.Contexts, f.EntitlementReads.Object,
                f.ProfileReads.Object, f.LocationReads.Object, f.OfferingReads.Object, dynamicClock.Object, f.HoursReads.Object,
                Microsoft.Extensions.Options.Options.Create(new NexFlow.Application.Features.Automation.ProcessMessage.Services.AssistantRecoveryOptions()));
        }
    }
}
