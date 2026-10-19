using System.Globalization;
using Microsoft.Extensions.Options;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.Reservation;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.LocationAvailability;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class AssistantRc11Tests
{
    [Theory]
    [InlineData("Aviso: el horario anterior está ocupado.\n• Lunes: 09:00–18:00\nServicios:\n• Servicio A\n• Servicio B\nAclaración: la reserva aún no está confirmada.\n¿Cuál servicio deseas reservar?")]
    [InlineData("Servicios disponibles:\n• Servicio A\n• Servicio B\nInformación adicional:\n• Trae tu documento de identidad.\n¿Cuál servicio deseas reservar?")]
    [InlineData("Advertencia: el PDF todavía no está disponible.\n• Conserva tu código de atención.\n¿Cuál servicio deseas reservar?")]
    public async Task Decoration_preserves_facts_warnings_and_final_question_while_replacing_only_the_actual_picker(string response)
    {
        var f = new AssistantConversationTests.Fixture(); await f.SeedDraft("RESERVATION");
        var c = await f.Context(); c.CurrentStep = "COLLECT_SERVICE"; c.SelectedServiceId = null;
        var before = AssistantConversationTests.Fixture.BusinessDraft(c);
        var decorated = await f.Recovery.DecorateAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, c, f.Modules, response, default);
        foreach (var line in response.Split('\n').Where(line => line is not ("• Servicio A" or "• Servicio B"))) Assert.Contains(line, decorated);
        Assert.Equal(1, decorated.Split("Servicio A").Length - 1); Assert.Equal(1, decorated.Split("Servicio B").Length - 1);
        Assert.Contains("1. Servicio A\n2. Servicio B", decorated);
        Assert.EndsWith("¿Cuál servicio deseas reservar?", decorated);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(c)); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Two_workspace_profiles_control_their_own_utc_availability_display_selection_and_commit()
    {
        var f = new BookingFixture(); var lima = f.AddWorkspace("America/Lima"); var madrid = f.AddWorkspace("Europe/Madrid");
        await f.SelectAndConfirm(lima, "2030-03-30", Utc("2030-03-30T14:00:00Z"));
        await f.SelectAndConfirm(madrid, "2030-03-30", Utc("2030-03-30T08:00:00Z"));
        Assert.Equal(2, f.Created.Count); Assert.Equal(new[] { lima, madrid }, f.Created.Select(r => r.WorkspaceId));
    }

    [Theory]
    [InlineData("2030-03-30", "2030-03-30T08:00:00Z")]
    [InlineData("2030-03-31", "2030-03-31T07:00:00Z")]
    public async Task The_dst_transition_changes_utc_without_changing_the_consumers_local_choice(string date, string expected)
    {
        var f = new BookingFixture(); await f.SelectAndConfirm(f.AddWorkspace("Europe/Madrid"), date, Utc(expected));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("invalid/zone")]
    public async Task Missing_or_invalid_workspace_zone_uses_the_same_lima_fallback_as_booking_and_engine(string? zone)
    {
        var f = new BookingFixture(); await f.SelectAndConfirm(f.AddWorkspace(zone), "2030-03-30", Utc("2030-03-30T14:00:00Z"));
    }

    private static DateTime Utc(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed class BookingFixture
    {
        private const string Phone = "+51999999999";
        private readonly Dictionary<Guid, string?> _zones = new();
        private readonly Mock<IReservationRepository> _reservations = new(MockBehavior.Strict);
        private readonly Mock<IOfferingService> _offerings = new(MockBehavior.Strict);
        private readonly Mock<IBusinessProfileRepository> _profiles = new(MockBehavior.Strict);
        private readonly IContextRecoveryService _contexts;
        private readonly ConversationRecoveryService _recovery;
        private readonly BookingFlow _booking;
        private readonly ReservationEngine _engine;
        private readonly HashSet<string> _modules = new() { "RESERVATIONS", "SERVICES" };
        public List<Reservation> Created { get; } = new();

        public BookingFixture()
        {
            var db = new InMemoryFirestore(); var states = new FirestoreConversationStateRepository(db.Db);
            var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(Utc("2030-03-01T00:00:00Z"));
            _contexts = new ContextRecoveryService(Mock.Of<IConversationCache>(), states, clock.Object);
            _profiles.Setup(p => p.GetProfileAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), default)).ReturnsAsync((Guid w, CancellationToken _) =>
                new BusinessProfileDto("Negocio autorizado", "", "", "", "", _zones[w]!));
            var service = new ServiceDto { Id = "service", Name = "Servicio real", DurationInMinutes = 30, RequiresReservation = true, IsActive = true };
            var catalog = new Mock<ICatalogRepository>(MockBehavior.Strict);
            catalog.Setup(c => c.GetItemByIdAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), "service", default)).ReturnsAsync(service);
            _offerings.Setup(o => o.GetServiceByIdAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), "service", default)).ReturnsAsync(service);
            _offerings.Setup(o => o.IsServiceAvailableAtLocationAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), "service", "location", default)).ReturnsAsync(true);
            var locations = new Mock<ILocationRepository>(MockBehavior.Strict);
            locations.Setup(l => l.GetLocationsAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), default)).ReturnsAsync(new[] { new LocationDto("location", "Sede real", "Dirección", null, null, true) });
            var hours = new Mock<IBusinessHoursRepository>(MockBehavior.Strict);
            hours.Setup(h => h.GetBusinessHoursAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), "location", default)).ReturnsAsync(Enumerable.Range(0, 7).Select(d => new BusinessHoursDto(d, "09:00", "11:00", false)));
            var availability = new Mock<ILocationAvailabilityService>(MockBehavior.Strict); availability.Setup(a => a.IsOfferingAvailableAtLocation(service, "location")).Returns(true);
            _reservations.Setup(r => r.GetReservationsForDateAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), "location", It.IsAny<DateTime>(), It.IsAny<DateTime>(), default)).ReturnsAsync(Array.Empty<Reservation>());
            _reservations.Setup(r => r.GetBySourceMessageIdAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), It.IsAny<string>(), default)).ReturnsAsync((Guid w, string source, CancellationToken _) => Created.SingleOrDefault(r => r.WorkspaceId == w && r.SourceMessageId == source));
            _reservations.Setup(r => r.IsTimeSlotAvailableAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), "location", It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(true);
            _reservations.Setup(r => r.Add(It.IsAny<Reservation>())).Callback<Reservation>(Created.Add);
            var uow = new Mock<IUnitOfWork>(MockBehavior.Strict); uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
            var outbox = new Mock<IOutboxRepository>(MockBehavior.Strict); outbox.Setup(o => o.AddAsync(It.IsAny<OutboxMessage>(), default)).Returns(Task.CompletedTask);
            _engine = new(_reservations.Object, catalog.Object, hours.Object, _profiles.Object, locations.Object, uow.Object, availability.Object, clock.Object, outbox.Object, new NexFlow.Tests.Fakes.InMemoryReservationResourceCoordinator());
            var entitlements = new Mock<IEntitlementService>(); entitlements.Setup(e => e.GetAvailableModuleCodesAsync(It.Is<Guid>(w => _zones.ContainsKey(w)), default)).ReturnsAsync(_modules);
            _booking = new(_offerings.Object, new LocationResolverService(locations.Object), _engine, locations.Object, hours.Object, entitlements.Object, _contexts, _reservations.Object, _profiles.Object, Mock.Of<IKnowledgeService>(MockBehavior.Strict));
            _recovery = new(_contexts, states, _offerings.Object, locations.Object, hours.Object, _engine, clock.Object, Options.Create(new AssistantRecoveryOptions()), _profiles.Object);
        }

        public Guid AddWorkspace(string? zone) { var id = Guid.NewGuid(); _zones.Add(id, zone); return id; }
        public async Task SelectAndConfirm(Guid workspace, string date, DateTime expectedUtc)
        {
            var c = new ConversationContextDto { CurrentGoal = "RESERVATION", CurrentStep = "COLLECT_TIME", SelectedLocationId = "location", SelectedServiceId = "service", RealCustomerName = "Ana Pérez", TargetDate = date };
            var slots = (await _engine.GetAvailabilityAsync(workspace, "location", "service", DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), default)).ToArray();
            Assert.Equal(expectedUtc, slots[0].StartTime);
            var original = await _booking.GetContinuationAsync(workspace, c, default);
            Assert.Contains("• 09:00", original);
            var decorated = await _recovery.DecorateAsync(workspace, Phone, c, _modules, original, default);
            Assert.Contains("1. 09:00", decorated); Assert.Equal("09:00", c.RecoveryOptions[0].Key);
            var selection = await _recovery.RecoverAsync(workspace, Phone, "chat", new("instance", Phone, "Ana", "1", $"select-{workspace}", false, workspace), c, _modules, default);
            Assert.Equal("09:00", selection!.Interpretation!.Time);
            var confirmation = await _booking.ProcessAsync(workspace, Phone, "chat", c, selection.Interpretation, "Ana", $"select-{workspace}", default);
            Assert.Contains("09:00", confirmation); Assert.DoesNotContain(Created, r => r.WorkspaceId == workspace);
            var source = $"confirm-{workspace}";
            await _booking.ProcessAsync(workspace, Phone, "chat", c, new AiInterpretation { Intent = ConversationIntent.Reservation, Confirmed = true }, "Ana", source, default);
            var row = Assert.Single(Created, r => r.WorkspaceId == workspace);
            Assert.Equal(expectedUtc, row.StartTime); Assert.Equal(expectedUtc.AddMinutes(30), row.EndTime); Assert.Equal(Phone, row.CustomerIdentifier);
            Assert.Null(c.CurrentGoal);
        }
    }
}
