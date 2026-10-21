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
    public async Task Single_future_appointment_reschedules_same_id_without_recollecting_commercial_data()
    {
        var f = new Fixture(); var original = f.Add(); var id = original.Id;
        await f.Turn("quiero editar mi reserva", "start");
        Assert.Contains("23/10/2026", f.Text("start")); Assert.Contains("Sede Concepcion", f.Text("start"));
        await f.Turn("Mejor para el martes que viene, por la mañana", "date");
        Assert.Empty(f.Events); Assert.Contains("27/10/2026", f.Text("date"));
        await f.Turn("1", "time"); Assert.Empty(f.Events); Assert.Contains("¿Confirmas", f.Text("time"));
        await f.Turn("sí confirmo", "confirm"); await f.Turn("sí confirmo", "confirm");
        Assert.Equal(id, Assert.Single(f.Rows.Values).Id); Assert.Equal(new DateTime(2026, 10, 27, 15, 0, 0, DateTimeKind.Utc), original.StartTime);
        Assert.Equal("service-a", original.ServiceId); Assert.Equal("sede-a", original.LocationId); Assert.Equal("Ana Pérez", original.CustomerName);
        Assert.Single(f.Events); Assert.Equal(1, f.Saves); Assert.Equal(0, f.Dialog.AiCalls); Assert.Empty(f.Dialog.Bookings);
    }

    [Fact]
    public async Task Several_appointments_page_with_current_references_and_revalidate_selection()
    {
        var f = new Fixture(); for (var i = 0; i < 7; i++) f.Add(day: 23 + i);
        await f.Turn("quiero ver mis citas", "page1");
        Assert.Equal(5, (await f.Dialog.Context()).ReservationManagement!.Options.Count);
        Assert.Contains("ver más", f.Text("page1")); Assert.DoesNotContain(Assert.Single(f.Rows.Values.Take(1)).Id.ToString(), f.Text("page1"));
        await f.Turn("ver más", "page2"); Assert.Equal(2, (await f.Dialog.Context()).ReservationManagement!.Options.Count);
        var selected = (await f.Dialog.Context()).ReservationManagement!.Options[0]; f.Rows[selected.Id].Cancel();
        await f.Turn("1", "stale"); Assert.Contains("no está vigente", f.Text("stale")); Assert.Empty(f.Events);
    }

    [Fact]
    public async Task Zero_foreign_expired_and_shared_contacts_do_not_disclose_private_appointments()
    {
        var f = new Fixture(); f.Add(contact: "+51888888888"); f.Add(workspace: Guid.NewGuid()); f.Add(day: 19);
        await f.Turn("quiero ver mis citas", "none"); Assert.Contains("No identifiqué", f.Text("none"));
        var one = f.Add(); f.Add(day: 24, name: "Otra persona");
        await f.Turn("quiero ver mis citas", "shared"); Assert.Contains("verificación", f.Text("shared"));
        Assert.DoesNotContain("23/10", f.Text("shared")); Assert.Empty(f.Events);
    }

    [Fact]
    public async Task Hour_only_change_preserves_date_and_cancel_is_a_separate_confirmed_operation()
    {
        var f = new Fixture(); var row = f.Add();
        await f.Turn("quiero cambiar el horario de mi cita", "start"); await f.Turn("16:00", "time");
        await f.Turn("no confirmo", "no"); Assert.Empty(f.Events); Assert.Contains("No he realizado cambios", f.Text("no"));
        await f.Turn("16:00", "time2"); await f.Turn("sí confirmos", "ambiguous"); Assert.Empty(f.Events);
        await f.Turn("sí confirmo", "yes"); Assert.Equal(new DateTime(2026, 10, 23, 21, 0, 0, DateTimeKind.Utc), row.StartTime);
        await f.Turn("quiero cancelar mi cita", "cancel-start"); Assert.Equal(ReservationStatus.Confirmed, row.Status);
        await f.Turn("no", "cancel-no"); Assert.Equal(ReservationStatus.Confirmed, row.Status); Assert.Single(f.Events);
        await f.Turn("quiero cancelar mi cita", "cancel-again"); await f.Turn("sí confirmo", "cancel-yes");
        Assert.Equal(ReservationStatus.Cancelled, row.Status); Assert.Equal(2, f.Events.Count);
    }

    [Fact]
    public async Task Information_interruption_expiry_revocation_and_handoff_preserve_original_reservation()
    {
        var f = new Fixture(); var row = f.Add(); var start = row.StartTime;
        await f.Turn("quiero editar mi reserva", "start");
        await f.Turn("cuáles son sus horarios", "info"); Assert.Contains("Dato real autorizado", f.Text("info"));
        Assert.NotNull((await f.Dialog.Context()).ReservationManagement);
        f.Dialog.Now = f.Dialog.Now.AddMinutes(11); await f.Turn("16:00", "expired"); Assert.Contains("no es válida", f.Text("expired"));
        Assert.Equal(start, row.StartTime); Assert.Empty(f.Events);
        await f.Turn("quiero editar mi reserva", "new"); f.Dialog.Modules.Remove("RESERVATIONS");
        await f.Turn("16:00", "denied"); Assert.Contains("no tiene habilitadas las reservas", f.Text("denied")); Assert.Empty(f.Events);
        f.Dialog.Modules.Add("RESERVATIONS"); await f.Turn("asesor", "human"); await f.Turn("sí confirmo", "after-human");
        Assert.Equal(1, f.Dialog.Handoffs); Assert.False(f.Dialog.Sent.ContainsKey("after-human")); Assert.Equal(start, row.StartTime);
    }

    [Fact]
    public async Task Lost_ack_resumes_durable_receipt_and_changed_payload_conflicts_without_another_event()
    {
        var f = new Fixture(); var row = f.Add(); await f.Turn("quiero editar mi reserva", "start"); await f.Turn("16:00", "time");
        f.LoseAck = true; await Assert.ThrowsAsync<TimeoutException>(() => f.Turn("sí confirmo", "confirm"));
        Assert.Single(f.Events); Assert.Equal(1, f.Saves); f.LoseAck = false;
        var pending = (await f.Dialog.Context()).ReservationManagement!.PendingCommand!;
        await f.Turn("sí confirmo", "confirm"); Assert.Contains("ya fue procesada", f.Text("confirm"));
        var conflict = await f.RealEngine!.ApplyExistingOperationAsync(pending with { ProposedUtc = pending.ProposedUtc!.Value.AddHours(1) }, default);
        Assert.Equal("Reservation.IdempotencyConflict", conflict.Error.Code); Assert.Single(f.Events); Assert.Equal(1, f.Saves);
        await f.Turn("quiero editar mi reserva", "next"); await f.Turn("10:00", "another-time"); await f.Turn("sí confirmo", "another-confirm");
        Assert.Equal(2, f.Saves); Assert.Equal(2, f.Events.Count); Assert.Equal(row.Id, Assert.Single(f.Rows.Values).Id);
    }

    [Fact]
    public async Task Unknown_delivery_and_old_replay_do_not_repeat_or_discard_a_newer_proposal()
    {
        var f = new Fixture(); f.Add(); await f.Turn("quiero editar mi reserva", "start"); await f.Turn("16:00", "time");
        f.Dialog.DeliveryStatus = MessageStatus.UnknownDelivery;
        await f.Turn("sí confirmo", "confirmed"); await f.Turn("sí confirmo", "confirmed");
        Assert.Single(f.Events); Assert.Equal(1, f.Saves); Assert.Equal(MessageStatus.UnknownDelivery, f.Dialog.Sent["confirmed"].Status);
        await f.Turn("quiero editar mi reserva", "new-proposal"); var selected = (await f.Dialog.Context()).ReservationManagement!.Selected!.Id;
        f.Dialog.Sent.Remove("confirmed"); // Simulates context/outbound retention boundary, not a provider resend.
        await f.Turn("sí confirmo", "confirmed");
        Assert.Equal(selected, (await f.Dialog.Context()).ReservationManagement!.Selected!.Id); Assert.Single(f.Events); Assert.Equal(1, f.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_committed_between_initial_lookup_and_selection_validation_is_replayed(bool cancel)
    {
        var f = new Fixture(); var row = f.Add();
        await f.Turn(cancel ? "quiero cancelar mi cita" : "quiero editar mi reserva", "start");
        if (!cancel) await f.Turn("16:00", "time");
        f.LoseAck = true; await Assert.ThrowsAsync<TimeoutException>(() => f.Turn("sí confirmo", "confirmed"));
        var command = (await f.Dialog.Context()).ReservationManagement!.PendingCommand!;
        f.MissFirstReceiptRead = true;
        var result = await f.RealEngine!.ApplyExistingOperationAsync(command, default);
        Assert.True(result.IsSuccess); Assert.Equal(row.Id, result.Value.Id);
        Assert.Equal(cancel ? "Cancelled" : "Confirmed", result.Value.Status);
        Assert.Single(f.Events); Assert.Equal(1, f.Saves); Assert.False(f.MissFirstReceiptRead);
    }

    [Fact]
    public async Task Information_choice_does_not_confirm_an_existing_reservation_mutation()
    {
        var f = new Fixture(); f.Add(); await f.Turn("quiero editar mi reserva", "start"); await f.Turn("16:00", "time");
        f.Dialog.KnowledgeReads.Setup(k => k.QueryAsync(f.Dialog.Workspace, It.IsAny<NexFlow.Application.Features.Knowledge.BusinessKnowledgeSnapshot>(),
            It.IsAny<NexFlow.Application.Features.Knowledge.KnowledgeQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NexFlow.Application.Features.Knowledge.KnowledgeResult {
                Status = NexFlow.Application.Features.Knowledge.KnowledgeStatus.Found, Source = NexFlow.Application.Features.Knowledge.KnowledgeTopic.BusinessHours,
                Facts = "¿De qué sede deseas horarios?", LocationChoices = [new("sede-a", "Sede Concepcion"), new("sede-b", "Sede San Jeronimo")]
            });
        await f.Turn("horarios generales", "info"); await f.Turn("sí confirmo", "yes-to-info");
        Assert.Empty(f.Events); Assert.Equal(0, f.Saves); Assert.Contains("¿De qué sede", f.Text("yes-to-info"));
    }

    [Fact]
    public async Task Third_party_request_and_changed_contact_binding_fail_without_disclosure_or_mutation()
    {
        var f = new Fixture(); f.Add(); await f.Turn("quiero revisar la cita de mi madre", "third-party");
        Assert.Contains("verificación", f.Text("third-party")); Assert.DoesNotContain("23/10", f.Text("third-party"));
        await f.Turn("quiero editar mi reserva", "start"); var state = await f.Dialog.Context();
        state.ReservationManagement!.ContactHash = ReservationOperationCommand.Hash("another-contact");
        await f.Dialog.Contexts.SaveContextAsync(f.Dialog.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.Turn("16:00", "wrong-binding"); Assert.Contains("no es válida", f.Text("wrong-binding")); Assert.Empty(f.Events);
    }

    [Fact]
    public async Task Slot_taken_after_summary_is_not_committed_and_returns_real_alternatives()
    {
        var f = new Fixture(); var original = f.Add(); await f.Turn("quiero editar mi reserva", "start"); await f.Turn("16:00", "propose");
        f.Add(contact: "+51888888888", hour: 21);
        f.Dialog.EngineReads.Setup(e => e.GetAvailabilityAsync(f.Dialog.Workspace, "sede-a", "service-a", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new TimeSlotDto(new(2026, 10, 23, 22, 0, 0, DateTimeKind.Utc), new(2026, 10, 23, 22, 30, 0, DateTimeKind.Utc), true) });
        await f.Turn("sí confirmo", "confirm");
        Assert.Empty(f.Events); Assert.Equal(0, f.Saves); Assert.Equal(15, original.StartTime.Hour);
        Assert.Contains("17:00", f.Text("confirm")); Assert.DoesNotContain("fue reagendada", f.Text("confirm"));
    }

    [Fact]
    public async Task Registered_management_pauses_and_restores_an_existing_draft_without_reviving_an_expired_one()
    {
        var f = new Fixture(); f.Add(); await f.Dialog.SeedDraft("RESERVATION");
        await f.Turn("quiero editar mi reserva", "draft-edit");
        Assert.Null((await f.Dialog.Context()).ReservationManagement); Assert.Empty(f.Events);
        var old = await f.Dialog.Context(); var name = old.RealCustomerName; var service = old.SelectedServiceId;
        f.Dialog.Now = f.Dialog.Now.AddDays(1).AddMinutes(1);
        await f.Turn("quiero editar mi reserva confirmada", "start");
        var active = await f.Dialog.Context(); Assert.Equal("RESERVATION_MANAGEMENT", active.CurrentGoal); Assert.Null(active.CurrentStep);
        Assert.Equal(name, active.RealCustomerName); Assert.Equal(service, active.SelectedServiceId);
        await f.Turn("cancelar", "leave"); var restored = await f.Dialog.Context();
        Assert.Equal("RESERVATION", restored.CurrentGoal); Assert.Equal(old.CurrentStep, restored.CurrentStep);
        Assert.Equal("EXPIRE", restored.PendingRecoveryAction); Assert.Equal(name, restored.RealCustomerName); Assert.Empty(f.Events);
    }

    [Fact]
    public async Task Concurrent_management_requests_have_one_context_cas_winner_without_business_effects()
    {
        var f = new Fixture(); f.Add();
        var profile = await f.Dialog.ProfileReads.Object.GetProfileAsync(f.Dialog.Workspace, default);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var reads = 0;
        f.Dialog.ProfileReads.Setup(p => p.GetProfileAsync(f.Dialog.Workspace, It.IsAny<CancellationToken>())).Returns(async () => {
            if (Interlocked.Increment(ref reads) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); return profile;
        });
        async Task<bool> Attempt(string source)
        {
            try { await f.Turn("quiero editar mi reserva", source); return true; }
            catch (NexFlow.Domain.Exceptions.ConcurrencyException) { return false; }
        }
        var results = await Task.WhenAll(Attempt("click-1"), Attempt("click-2"));
        Assert.Single(results, success => success); Assert.NotNull((await f.Dialog.Context()).ReservationManagement);
        Assert.Single(f.Rows); Assert.Empty(f.Events); Assert.Equal(0, f.Saves);
    }

    [Fact]
    public async Task Reschedule_availability_excludes_only_the_owned_appointment_not_other_contacts()
    {
        var f = new Fixture(); var row = f.Add(durationMinutes: 60); f.Add(contact: "+51888888888", hour: 16);
        var date = new DateTime(2026, 10, 23);
        var normal = await f.RealEngine!.GetAvailabilityAsync(f.Dialog.Workspace, "sede-a", "service-a", date, default);
        var editing = await f.RealEngine.GetAvailabilityForExistingAsync(f.Dialog.Workspace, AssistantConversationTests.Fixture.Phone, row.Id, date, default);
        var overlappingOwn = new DateTime(2026, 10, 23, 15, 30, 0, DateTimeKind.Utc);
        Assert.DoesNotContain(normal, s => s.StartTime == overlappingOwn);
        Assert.Contains(editing, s => s.StartTime == overlappingOwn);
        Assert.DoesNotContain(editing, s => s.StartTime == overlappingOwn.AddMinutes(30));
        Assert.Empty(await f.RealEngine.GetAvailabilityForExistingAsync(f.Dialog.Workspace, AssistantConversationTests.Fixture.Phone, f.Rows.Values.Single(r => r.Id != row.Id).Id, date, default));
        Assert.Empty(f.Events); Assert.Equal(0, f.Saves);
    }

    private sealed class Fixture
    {
        internal AssistantConversationTests.Fixture Dialog { get; }
        internal Dictionary<Guid, Reservation> Rows { get; } = [];
        internal Dictionary<string, OutboxMessage> Events { get; } = [];
        internal int Saves { get; private set; }
        internal bool LoseAck { get; set; }
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
                    var rows = Future(w, p).Where(r => after == null || r.StartTime > after.StartTime || r.StartTime == after.StartTime && r.Id.CompareTo(after.Id) > 0).OrderBy(r => r.StartTime).ThenBy(r => r.Id).Take(6).ToArray();
                    return new ReservationPage(rows.Take(5).Select(Capture).ToArray(), rows.Length > 5);
                });
            repo.Setup(r => r.GetOwnedFutureAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid w, string p, Guid id, CancellationToken _) => Future(w, p).Where(r => r.Id == id).Select(Capture).SingleOrDefault());
            repo.Setup(r => r.HasSingleFutureOwnerAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<CancellationToken>()))
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
                    var result = await RealEngine.ApplyExistingOperationAsync(command, ct); if (LoseAck && result.IsSuccess) throw new TimeoutException("synthetic commit acknowledgement lost"); return result;
                });
            f.EngineReads.Setup(e => e.GetAvailabilityForExistingAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns((Guid w, string _, Guid id, DateTime date, CancellationToken ct) => f.EngineReads.Object.GetAvailabilityAsync(w, Rows[id].LocationId, Rows[id].ServiceId, date, ct));
            return new ExistingReservationFlow(repo.Object, f.EngineReads.Object, outbox.Object, f.Contexts, f.EntitlementReads.Object,
                f.ProfileReads.Object, f.LocationReads.Object, f.OfferingReads.Object, dynamicClock.Object, f.HoursReads.Object,
                Microsoft.Extensions.Options.Options.Create(new NexFlow.Application.Features.Automation.ProcessMessage.Services.AssistantRecoveryOptions()));
        }
    }
}
