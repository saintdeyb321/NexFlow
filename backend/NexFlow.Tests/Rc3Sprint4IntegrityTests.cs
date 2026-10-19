using Microsoft.AspNetCore.Mvc;
using Moq;
using NexFlow.API.Controllers.Business;
using NexFlow.API.Controllers.Reservations;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.Reservation;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.LocationAvailability;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3Sprint4IntegrityTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Service_soft_delete_preserves_history_and_blocks_future_confirmed_reservations(bool future)
    {
        var f = await Fixture.Create(); f.FutureLocations = future ? ["location"] : [];
        var response = await f.Services.DeleteService("service", default);
        if (future) Assert.IsType<ConflictObjectResult>(response); else Assert.IsType<NoContentResult>(response);
        var service = (ServiceDto)(await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!;
        Assert.Equal(future, service.IsActive); Assert.Equal("service", service.Id); Assert.Equal("Service", service.Name);
    }

    [Theory]
    [InlineData("inactive", true)] [InlineData("not-reservable", true)] [InlineData("duration", true)]
    [InlineData("remove-location", true)] [InlineData("retain-location", false)] [InlineData("rename", false)]
    public async Task Updates_reject_only_changes_invalidating_future_reservations(string change, bool rejected)
    {
        var f = await Fixture.Create(); f.FutureLocations = ["location"];
        var service = (ServiceDto)(await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!;
        switch (change)
        {
            case "inactive": service.IsActive = false; break;
            case "not-reservable": service.RequiresReservation = false; break;
            case "duration": service.DurationInMinutes = 60; break;
            case "remove-location": service.LocationScope = "SPECIFIC"; service.LocationIds = ["second"]; break;
            case "retain-location": service.LocationScope = " specific "; service.LocationIds = [" location "]; break;
            case "rename": service.Name = "Renamed"; break;
        }
        var result = await f.Services.UpdateService("service", service, default);
        if (rejected) Assert.IsType<ConflictObjectResult>(result); else Assert.IsType<OkObjectResult>(result);
        var persisted = (ServiceDto)(await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!;
        Assert.True(persisted.IsActive); Assert.True(persisted.RequiresReservation); Assert.Equal(30, persisted.DurationInMinutes);
        Assert.Equal(change == "rename" ? "Renamed" : "Service", persisted.Name);
    }

    [Fact]
    public async Task Same_service_id_in_another_workspace_does_not_block_own_changes_and_denied_license_writes_nothing()
    {
        var f = await Fixture.Create(); var other = Guid.NewGuid();
        await f.Catalog.SaveItemAsync(other, Fixture.Service(), true, default);
        f.Reservations.Setup(r => r.GetFutureConfirmedServiceLocationsAsync(other, "service", default)).ReturnsAsync(new[] { "location" });
        Assert.IsType<NoContentResult>(await f.Services.DeleteService("service", default));
        Assert.True((await f.Catalog.GetItemByIdAsync(other, "service", default))!.IsActive);
        f.Entitlements.Setup(e => e.GetAvailableModuleCodesAsync(f.Workspace, default)).ReturnsAsync(Array.Empty<string>());
        Assert.Equal(403, Assert.IsType<ObjectResult>(await f.Services.UpdateService("service", Fixture.Service(), default)).StatusCode);
        Assert.False((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive);
        f.Reservations.Verify(r => r.GetFutureConfirmedServiceLocationsAsync(other, "service", default), Times.Never);
    }

    [Fact]
    public async Task Reactivation_restores_availability_without_restoring_or_rewriting_old_reservations()
    {
        var f = await Fixture.Create(); await f.Services.DeleteService("service", default);
        Assert.Empty(await f.Engine.GetAvailabilityAsync(f.Workspace, "location", "service", Fixture.Day, default));
        Assert.IsType<OkObjectResult>(await f.Services.UpdateService("service", Fixture.Service(), default));
        Assert.NotEmpty(await f.Engine.GetAvailabilityAsync(f.Workspace, "location", "service", Fixture.Day, default));
        Assert.Empty(f.Rows);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Web_and_bot_creation_share_inactive_service_rules(bool bot)
    {
        var f = await Fixture.Create(); await f.Services.DeleteService("service", default);
        if (bot)
        {
            var flow = new BookingFlow(new OfferingService(f.Catalog), new LocationResolverService(f.Locations), f.Engine,
                f.Locations, Mock.Of<IBusinessHoursRepository>(), f.Entitlements.Object, Mock.Of<IContextRecoveryService>(),
                f.Reservations.Object, Mock.Of<IBusinessProfileRepository>(), Mock.Of<IKnowledgeService>());
            var draft = new ConversationContextDto { CurrentGoal = "RESERVATION", SelectedLocationId = "location", SelectedServiceId = "service",
                RealCustomerName = "Customer", TargetDate = "2030-07-02", TargetTime = "10:00" };
            var response = await flow.ProcessAsync(f.Workspace, "customer", "conversation", draft, new AiInterpretation { Confirmed = true }, "Customer", "inbound", default);
            Assert.Contains("ya no está disponible", response); Assert.Null(draft.SelectedServiceId);
        }
        else
        {
            var controller = new ReservationsController(f.Engine, f.Reservations.Object,
                Mock.Of<IWorkspaceContext>(w => w.CurrentWorkspaceId == f.Workspace), f.Entitlements.Object);
            var response = Assert.IsType<ObjectResult>(await controller.CreateReservation(new("location", "service", "customer", "Customer", Fixture.Day.AddHours(15)), default));
            Assert.Equal(400, response.StatusCode);
            Assert.Contains("Service.Inactive", System.Text.Json.JsonSerializer.Serialize(response.Value));
        }
        Assert.Empty(f.Rows);
    }

    [Fact]
    public async Task Deleted_location_has_no_availability_and_cannot_be_used_for_creation_or_rescheduling()
    {
        var f = await Fixture.Create(); var booked = await f.Book(); Assert.True(booked.IsSuccess);
        // Model an already committed deletion/legacy orphan, without pretending to pass its SQL guard.
        Assert.True((await f.Locations.DeleteLocationAsync(f.Workspace, "location", default)).IsSuccess);
        Assert.Empty(await f.Engine.GetAvailabilityAsync(f.Workspace, "location", "service", Fixture.Day, default));
        Assert.Equal("Location.NotFound", (await f.Book()).Error.Code);
        Assert.Equal("Location.NotFound", (await f.Engine.EditReservationAsync(f.Workspace, booked.Value.Id, Fixture.Day.AddHours(16), default)).Error.Code);
        Assert.Single(f.Rows); Assert.Equal(Fixture.Day.AddHours(15), f.Rows[0].StartTime);
    }

    [Fact]
    public async Task Location_future_guard_and_firestore_references_hours_and_main_location_remain_intact()
    {
        var f = await Fixture.Create(); await f.Book();
        Assert.IsType<ConflictObjectResult>(await f.Business.DeleteLocation("location", f.Reservations.Object, default));
        Assert.Equal(2, (await f.Locations.GetLocationsAsync(f.Workspace, default)).Count());
        var service = Fixture.Service(); service.LocationScope = "SPECIFIC"; service.LocationIds = ["location"];
        await f.Catalog.SaveItemAsync(f.Workspace, service, false, default);
        Assert.True((await f.Locations.DeleteLocationAsync(f.Workspace, "location", default)).IsFailure);
        service.LocationScope = "ALL"; await f.Catalog.SaveItemAsync(f.Workspace, service, false, default);
        var schedule = $"workspaces/{f.Workspace}/hours/location/schedule/tuesday";
        f.Db.Seed(schedule, new Dictionary<string, Google.Cloud.Firestore.V1.Value> { ["DayOfWeek"] = new() { IntegerValue = 2 } });
        f.Rows.Clear();
        Assert.IsType<NoContentResult>(await f.Business.DeleteLocation("location", f.Reservations.Object, default));
        Assert.True(Assert.Single(await f.Locations.GetLocationsAsync(f.Workspace, default)).IsMain);
        Assert.Null(f.Db.Read(schedule));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Characterization_cross_store_guard_has_a_residual_race_not_a_distributed_transaction(bool deleteLocation)
    {
        var f = await Fixture.Create();
        var checkedSql = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (deleteLocation)
            f.Reservations.Setup(r => r.HasFutureConfirmedAtLocationAsync(f.Workspace, "location", default)).Returns(async () =>
            { checkedSql.SetResult(); await resume.Task; return false; });
        else
            f.Reservations.Setup(r => r.GetFutureConfirmedServiceLocationsAsync(f.Workspace, "service", default)).Returns(async () =>
            { checkedSql.SetResult(); await resume.Task; return Array.Empty<string>(); });
        var mutation = deleteLocation ? f.Business.DeleteLocation("location", f.Reservations.Object, default)
            : f.Services.DeleteService("service", default);
        await checkedSql.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await f.Book()).IsSuccess); // Booking commits after SQL check and before Firestore write.
        resume.SetResult(); Assert.IsType<NoContentResult>(await mutation);
        Assert.Single(f.Rows);
        if (deleteLocation) Assert.DoesNotContain(await f.Locations.GetLocationsAsync(f.Workspace, default), l => l.Id == "location");
        else Assert.False((await f.Catalog.GetItemByIdAsync(f.Workspace, "service", default))!.IsActive);
        // This green characterization explicitly records the unresolved window for approval;
        // it is not evidence that the business race is solved by these local fakes.
    }

    [Fact]
    public async Task Characterization_reservation_keeps_identifiers_and_utc_but_cannot_recover_original_service_name()
    {
        var f = await Fixture.Create(); var original = (await f.Book("source")).Value;
        f.FutureLocations = ["location"];
        var renamed = Fixture.Service(); renamed.Name = "New name";
        Assert.IsType<OkObjectResult>(await f.Services.UpdateService("service", renamed, default));
        var row = Assert.Single(f.Rows);
        Assert.Equal(original.ServiceId, row.ServiceId); Assert.Equal(original.LocationId, row.LocationId);
        Assert.Equal(original.DateTime, row.StartTime); Assert.Equal(DateTimeKind.Utc, row.StartTime.Kind);
        Assert.Equal("source", row.SourceMessageId); Assert.Equal(f.Workspace, row.WorkspaceId);
        Assert.Equal("New name", (await f.Catalog.GetItemByIdAsync(f.Workspace, row.ServiceId, default))!.Name);
        Assert.Null(typeof(Reservation).GetProperty("ServiceNameSnapshot"));
        // The UI resolves IDs against today's catalog: preserving the original label
        // requires an approved nullable historical snapshot, not an invented backfill.
    }

    private sealed class Fixture
    {
        public static readonly DateTime Day = new(2030, 7, 2, 0, 0, 0, DateTimeKind.Utc);
        public Guid Workspace { get; } = Guid.NewGuid();
        public InMemoryFirestore Db { get; } = new();
        public FirestoreCatalogRepository Catalog { get; }
        public FirestoreLocationRepository Locations { get; }
        public Mock<IReservationRepository> Reservations { get; } = new();
        public Mock<IEntitlementService> Entitlements { get; } = new();
        public IReadOnlyList<string> FutureLocations { get; set; } = [];
        public List<Reservation> Rows { get; } = [];
        public ServicesController Services { get; }
        public BusinessController Business { get; }
        public ReservationEngine Engine { get; }
        private Fixture()
        {
            Catalog = new(Db.Db); Locations = new(Db.Db);
            var workspace = Mock.Of<IWorkspaceContext>(w => w.CurrentWorkspaceId == Workspace);
            Entitlements.Setup(e => e.GetAvailableModuleCodesAsync(Workspace, default)).ReturnsAsync(new[] { "SERVICES", "LOCATIONS", "RESERVATIONS" });
            Reservations.Setup(r => r.GetFutureConfirmedServiceLocationsAsync(Workspace, "service", default)).ReturnsAsync(() => FutureLocations);
            Reservations.Setup(r => r.HasFutureConfirmedAtLocationAsync(Workspace, "location", default)).ReturnsAsync(() => Rows.Count > 0);
            Reservations.Setup(r => r.GetReservationsForDateAsync(Workspace, "location", It.IsAny<DateTime>(), It.IsAny<DateTime>(), default)).ReturnsAsync(() => Rows);
            Reservations.Setup(r => r.IsTimeSlotAvailableAsync(Workspace, "location", It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(true);
            Reservations.Setup(r => r.Add(It.IsAny<Reservation>())).Callback<Reservation>(Rows.Add);
            Reservations.Setup(r => r.GetByIdAsync(Workspace, It.IsAny<Guid>(), default)).ReturnsAsync((Guid _, Guid id, CancellationToken _) => Rows.SingleOrDefault(r => r.Id == id));
            var hours = new Mock<IBusinessHoursRepository>();
            hours.Setup(h => h.GetBusinessHoursAsync(Workspace, "location", default)).ReturnsAsync(new[] { new BusinessHoursDto(2, "09:00", "18:00", false) });
            var profiles = new Mock<IBusinessProfileRepository>();
            profiles.Setup(p => p.GetProfileAsync(Workspace, default)).ReturnsAsync(new BusinessProfileDto("Business", "", "", "", "", "America/Lima"));
            var uow = new Mock<IUnitOfWork>(); uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
            var clock = Mock.Of<IClock>(c => c.UtcNow == Day.AddDays(-1));
            Engine = new(Reservations.Object, Catalog, hours.Object, profiles.Object, Locations, uow.Object,
                new LocationAvailabilityService(Catalog, Locations), clock, Mock.Of<IOutboxRepository>());
            Services = new(Catalog, workspace, Entitlements.Object, Reservations.Object);
            Business = new(profiles.Object, Catalog, Mock.Of<IFaqRepository>(), Locations, hours.Object, workspace,
                Mock.Of<IWorkspaceRepository>(), uow.Object, Entitlements.Object);
        }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            Assert.True((await f.Locations.SaveLocationAsync(f.Workspace, new("location", "Main", "Address", null, null, true), true, 3, default)).IsSuccess);
            Assert.True((await f.Locations.SaveLocationAsync(f.Workspace, new("second", "Second", "Address 2", null, null, false), true, 3, default)).IsSuccess);
            await f.Catalog.SaveItemAsync(f.Workspace, Service(), true, default); return f;
        }
        public static ServiceDto Service() => new() { Id = "service", Name = "Service", IsActive = true, Currency = "PEN", DurationInMinutes = 30, RequiresReservation = true, LocationScope = "ALL" };
        public Task<NexFlow.Application.Common.Result<NexFlow.Application.Features.Reservations.ReservationDto>> Book(string? source = null) =>
            Engine.CreateReservationAsync(Workspace, "location", "service", "customer", "Customer", Day.AddHours(15), default, source);
    }
}
