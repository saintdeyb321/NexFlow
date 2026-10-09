using System.Globalization;
using System.Text.Json;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.Reservation;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.LocationAvailability;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using Xunit;

namespace NexFlow.Tests;

public sealed class ReservationsPeruTimeTests
{
    [Fact]
    public async Task Availability_uses_peru_civil_day_and_returns_unoccupied_utc_slots()
    {
        var f = new EngineFixture();
        f.HoursFor((int)DayOfWeek.Tuesday, "09:00", "11:00");
        var occupied = Reservation.Create(f.Workspace, EngineFixture.Location, EngineFixture.ServiceId,
            "other-customer", "Other", Time("2030-07-02T14:30:00Z"), Time("2030-07-02T15:00:00Z"));
        f.Reservations.Setup(r => r.GetReservationsForDateAsync(f.Workspace, EngineFixture.Location,
                Time("2030-07-02T05:00:00Z"), Time("2030-07-03T05:00:00Z"), default))
            .ReturnsAsync(new[] { occupied });

        var slots = (await f.Engine.GetAvailabilityAsync(f.Workspace, EngineFixture.Location,
            EngineFixture.ServiceId, Time("2030-07-02T00:00:00"), default)).ToArray();

        Assert.Equal(new[] { Time("2030-07-02T14:00:00Z"), Time("2030-07-02T15:00:00Z"),
            Time("2030-07-02T15:30:00Z") }, slots.Select(slot => slot.StartTime));
        Assert.All(slots, slot =>
        {
            Assert.Equal(DateTimeKind.Utc, slot.StartTime.Kind);
            Assert.Equal(DateTimeKind.Utc, slot.EndTime.Kind);
            Assert.Equal(slot.StartTime.AddMinutes(30), slot.EndTime);
            Assert.True(slot.IsAvailable);
        });
        f.Reservations.VerifyAll();
        f.VerifyNoWrites();
    }

    [Theory]
    [InlineData("2030-07-02T10:00:00")]
    [InlineData("2030-07-02T15:00:00Z")]
    public async Task Create_accepts_equivalent_peru_local_and_utc_without_double_conversion(string input)
    {
        var f = new EngineFixture();
        var expected = Time("2030-07-02T15:00:00Z");
        f.AvailableAt(expected, null);
        f.EnableWrites();

        var result = await f.CreateAsync(Time(input));

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(expected, result.Value.DateTime);
        Assert.Equal(DateTimeKind.Utc, result.Value.DateTime.Kind);
        var row = Assert.Single(f.Added);
        Assert.Equal(f.Workspace, row.WorkspaceId);
        Assert.Equal(EngineFixture.Location, row.LocationId);
        Assert.Equal(expected, row.StartTime);
        Assert.Equal(expected.AddMinutes(30), row.EndTime);
        f.VerifyPersistedEvent("RESERVATION_CREATED", expected);
        f.Reservations.VerifyAll();
    }

    [Theory]
    [InlineData("2030-07-03T02:00:00Z", (int)DayOfWeek.Tuesday)]
    [InlineData("2030-12-31T01:15:00Z", (int)DayOfWeek.Monday)]
    public async Task Create_utc_after_midnight_uses_previous_peru_day_business_hours(string input, int localDay)
    {
        var f = new EngineFixture();
        // Only the previous Peru day is open; the UTC day must not select its business hours.
        f.HoursFor(localDay, "20:00", "22:00");
        var expected = Time(input);
        f.AvailableAt(expected, null);
        f.EnableWrites();

        var result = await f.CreateAsync(expected);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(expected, Assert.Single(f.Added).StartTime);
        f.VerifyPersistedEvent("RESERVATION_CREATED", expected);
        f.Reservations.VerifyAll();
    }

    [Theory]
    [InlineData("2030-07-02T21:00:00", "2030-07-03T02:00:00Z", (int)DayOfWeek.Tuesday)]
    [InlineData("2030-07-03T02:00:00Z", "2030-07-03T02:00:00Z", (int)DayOfWeek.Tuesday)]
    [InlineData("2030-12-30T20:15:00", "2030-12-31T01:15:00Z", (int)DayOfWeek.Monday)]
    [InlineData("2030-12-31T01:15:00Z", "2030-12-31T01:15:00Z", (int)DayOfWeek.Monday)]
    public async Task Reschedule_local_and_utc_use_peru_day_and_preserve_utc_instant(string input, string expectedUtc, int localDay)
    {
        var f = new EngineFixture();
        f.HoursFor(localDay, "20:00", "22:00");
        var row = f.Existing();
        var expected = Time(expectedUtc);
        f.AvailableAt(expected, row.Id);
        f.EnableWrites(create: false);

        var result = await f.Engine.EditReservationAsync(f.Workspace, row.Id, Time(input), default);

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(row.Id, result.Value.Id);
        Assert.Equal(expected, result.Value.DateTime);
        Assert.Equal(DateTimeKind.Utc, row.StartTime.Kind);
        Assert.Equal(expected, row.StartTime);
        Assert.Equal(expected.AddMinutes(30), row.EndTime);
        Assert.Empty(f.Added);
        f.VerifyPersistedEvent("RESERVATION_RESCHEDULED", expected);
        f.Reservations.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_and_reschedule_reject_service_ending_after_peru_closing_time(bool reschedule)
    {
        var f = new EngineFixture();
        var row = reschedule ? f.Existing() : null;
        var input = Time("2030-07-02T23:00:00Z"); // 18:00 Peru; a 30-minute service cannot start at closing.

        var result = row == null ? await f.CreateAsync(input)
            : await f.Engine.EditReservationAsync(f.Workspace, row.Id, input, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Reservation.OutOfHours", result.Error.Code);
        if (row != null) Assert.Equal(Time("2030-07-01T15:00:00Z"), row.StartTime);
        f.Reservations.Verify(r => r.IsTimeSlotAvailableAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        f.VerifyNoWrites();
    }

    [Theory]
    [InlineData(false, "Reservation.ConcurrencyConflict")]
    [InlineData(true, "Reservation.Conflict")]
    public async Task Occupied_peru_slot_is_rejected_without_mutation_or_outbox(bool reschedule, string expectedCode)
    {
        var f = new EngineFixture();
        var row = reschedule ? f.Existing() : null;
        var expected = Time("2030-07-02T15:00:00Z");
        f.AvailableAt(expected, row?.Id, available: false);

        var result = row == null ? await f.CreateAsync(Time("2030-07-02T10:00:00"))
            : await f.Engine.EditReservationAsync(f.Workspace, row.Id, Time("2030-07-02T10:00:00"), default);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        if (row != null) Assert.Equal(Time("2030-07-01T15:00:00Z"), row.StartTime);
        f.Reservations.VerifyAll();
        f.VerifyNoWrites();
    }

    private static DateTime Time(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed class EngineFixture
    {
        public const string Location = "peru-location";
        public const string ServiceId = "peru-service";
        public Guid Workspace { get; } = Guid.NewGuid();
        public Mock<IReservationRepository> Reservations { get; } = new(MockBehavior.Strict);
        private Mock<ICatalogRepository> Catalog { get; } = new(MockBehavior.Strict);
        private Mock<IBusinessHoursRepository> Hours { get; } = new(MockBehavior.Strict);
        private Mock<IBusinessProfileRepository> Profiles { get; } = new(MockBehavior.Strict);
        private Mock<ILocationRepository> Locations { get; } = new(MockBehavior.Strict);
        private Mock<IUnitOfWork> UnitOfWork { get; } = new(MockBehavior.Strict);
        private Mock<ILocationAvailabilityService> LocationAvailability { get; } = new(MockBehavior.Strict);
        private Mock<IClock> Clock { get; } = new(MockBehavior.Strict);
        private Mock<IOutboxRepository> Outbox { get; } = new(MockBehavior.Strict);
        public List<Reservation> Added { get; } = new();
        private List<OutboxMessage> Events { get; } = new();
        public ReservationEngine Engine { get; }

        public EngineFixture()
        {
            var service = new ServiceDto { Id = ServiceId, Name = "Service", DurationInMinutes = 30,
                IsActive = true, RequiresReservation = true };
            Catalog.Setup(r => r.GetItemByIdAsync(Workspace, ServiceId, default)).ReturnsAsync(service);
            Profiles.Setup(r => r.GetProfileAsync(Workspace, default))
                .ReturnsAsync(new BusinessProfileDto("Peru business", "", "", "", "", "America/Lima"));
            Locations.Setup(r => r.GetLocationsAsync(Workspace, default))
                .ReturnsAsync(new[] { new LocationDto(Location, "Peru", "Lima", null, null, true) });
            LocationAvailability.Setup(r => r.IsOfferingAvailableAtLocation(service, Location)).Returns(true);
            Clock.SetupGet(c => c.UtcNow).Returns(Time("2030-07-01T23:00:00Z"));
            HoursFor((int)DayOfWeek.Tuesday, "09:00", "18:00");
            Engine = new(Reservations.Object, Catalog.Object, Hours.Object, Profiles.Object, Locations.Object,
                UnitOfWork.Object, LocationAvailability.Object, Clock.Object, Outbox.Object);
        }

        public void HoursFor(int day, string open, string close) => Hours
            .Setup(r => r.GetBusinessHoursAsync(Workspace, Location, default))
            .ReturnsAsync(new[] { new BusinessHoursDto(day, open, close, false) });

        public void AvailableAt(DateTime start, Guid? excluded, bool available = true) => Reservations
            .Setup(r => r.IsTimeSlotAvailableAsync(Workspace, Location, start, start.AddMinutes(30), excluded, default))
            .ReturnsAsync(available);

        public Reservation Existing()
        {
            var start = Time("2030-07-01T15:00:00Z");
            var row = Reservation.Create(Workspace, Location, ServiceId, "customer", "Customer", start, start.AddMinutes(30));
            Reservations.Setup(r => r.GetByIdAsync(Workspace, row.Id, default)).ReturnsAsync(row);
            return row;
        }

        public void EnableWrites(bool create = true)
        {
            if (create) Reservations.Setup(r => r.Add(It.IsAny<Reservation>())).Callback<Reservation>(Added.Add);
            Outbox.Setup(r => r.AddAsync(It.IsAny<OutboxMessage>(), default))
                .Callback<OutboxMessage, CancellationToken>((message, _) => Events.Add(message)).Returns(Task.CompletedTask);
            UnitOfWork.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        }

        public Task<NexFlow.Application.Common.Result<ReservationDto>> CreateAsync(DateTime input) => Engine
            .CreateReservationAsync(Workspace, Location, ServiceId, "customer", "Customer", input, default);

        public void VerifyPersistedEvent(string eventType, DateTime start)
        {
            var message = Assert.Single(Events);
            Assert.Equal(Workspace, message.WorkspaceId);
            Assert.Equal(eventType, message.EventType);
            using var payload = JsonDocument.Parse(message.PayloadJson);
            Assert.Equal(Workspace, payload.RootElement.GetProperty("WorkspaceId").GetGuid());
            Assert.Equal(start, payload.RootElement.GetProperty("Data").GetProperty("DateTime").GetDateTime());
            Outbox.Verify(r => r.AddAsync(message, default), Times.Once);
            Outbox.VerifyNoOtherCalls();
            UnitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
            UnitOfWork.VerifyNoOtherCalls();
        }

        public void VerifyNoWrites()
        {
            Assert.Empty(Added);
            Assert.Empty(Events);
            Reservations.Verify(r => r.Add(It.IsAny<Reservation>()), Times.Never);
            Outbox.VerifyNoOtherCalls();
            UnitOfWork.VerifyNoOtherCalls();
        }
    }
}
