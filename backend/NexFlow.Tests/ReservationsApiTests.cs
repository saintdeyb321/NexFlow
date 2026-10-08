using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using NexFlow.API.Controllers.Reservations;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests;

public sealed class ReservationsApiTests
{
    [Fact]
    public async Task Legacy_daily_http_contract_preserves_dto_and_all_historical_states()
    {
        await using var f = new ApiFixture();
        foreach (var state in Enum.GetValues<ReservationStatus>())
            f.Rows.Add(Row(f.Workspace, Utc("2018-07-02T05:00:00Z").AddHours(f.Rows.Count), state));
        var client = await f.StartAsync();

        using var response = await client.GetAsync("/api/reservations?locationId=location-a&date=2018-07-02");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(4, body.RootElement.GetArrayLength());
        var first = body.RootElement[0];
        Assert.Equal(new[] { "customerIdentifier", "customerName", "dateTime", "id", "locationId", "serviceId", "status", "workspaceId" },
            first.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(f.Workspace, first.GetProperty("workspaceId").GetGuid());
        Assert.Equal("2018-07-02T05:00:00Z", first.GetProperty("dateTime").GetString());
        Assert.Equal(new[] { "Cancelled", "Completed", "Confirmed", "Pending" },
            body.RootElement.EnumerateArray().Select(r => r.GetProperty("status").GetString()).Order(StringComparer.Ordinal).ToArray());
        var query = Assert.Single(f.Reads);
        Assert.Equal("location-a", query.Location);
        Assert.Equal(Utc("2018-07-02T05:00:00Z"), query.Start);
        Assert.Equal(Utc("2018-07-03T05:00:00Z"), query.End);
        f.Engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Weekly_http_query_crosses_year_in_one_read_and_uses_only_authenticated_workspace()
    {
        await using var f = new ApiFixture();
        for (var day = 0; day < 7; day++) f.Rows.Add(Row(f.Workspace, Utc("2026-12-28T05:00:00Z").AddDays(day), ReservationStatus.Confirmed));
        var client = await f.StartAsync();
        var foreignWorkspace = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/reservations?locationId=all&from=2026-12-28&to=2027-01-04&workspaceId={foreignWorkspace}");
        request.Headers.Add("X-Workspace-Id", foreignWorkspace.ToString());

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(7, body.RootElement.GetArrayLength());
        var query = Assert.Single(f.Reads);
        Assert.Equal(f.Workspace, query.Workspace);
        Assert.Null(query.Location);
        Assert.Equal(Utc("2026-12-28T05:00:00Z"), query.Start);
        Assert.Equal(Utc("2027-01-04T05:00:00Z"), query.End);
        f.Locations.VerifyNoOtherCalls();
        f.Engine.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("America/Lima", "2024-02-26", "2024-03-04", "2024-02-26T05:00:00Z", "2024-03-04T05:00:00Z")]
    [InlineData("Asia/Kathmandu", "2024-02-26", "2024-03-04", "2024-02-25T18:15:00Z", "2024-03-03T18:15:00Z")]
    [InlineData("America/New_York", "2026-03-02", "2026-03-09", "2026-03-02T05:00:00Z", "2026-03-09T04:00:00Z")]
    [InlineData("America/New_York", "2026-10-26", "2026-11-02", "2026-10-26T04:00:00Z", "2026-11-02T05:00:00Z")]
    [InlineData("America/Santiago", "2019-09-08", "2019-09-09", "2019-09-08T04:00:00Z", "2019-09-09T03:00:00Z")]
    [InlineData("America/Havana", "2020-11-01", "2020-11-02", "2020-11-01T04:00:00Z", "2020-11-02T05:00:00Z")]
    public async Task Civil_boundaries_use_business_zone_including_short_long_skipped_and_repeated_midnights(
        string zone, string from, string to, string start, string end)
    {
        await using var f = new ApiFixture { Zone = zone };

        Assert.IsType<OkObjectResult>(await f.ReadAsync(from: from, to: to));

        var query = Assert.Single(f.Reads);
        Assert.Equal(Utc(start), query.Start);
        Assert.Equal(Utc(end), query.End);
        Assert.Equal(DateTimeKind.Utc, query.Start.Kind);
        Assert.Equal(DateTimeKind.Utc, query.End.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not/a-zone")]
    public async Task Missing_empty_or_invalid_business_timezone_preserves_lima_fallback(string? zone)
    {
        await using var f = new ApiFixture { Zone = zone };
        Assert.IsType<OkObjectResult>(await f.ReadAsync(from: "2018-07-02", to: "2018-07-09"));
        var query = Assert.Single(f.Reads);
        Assert.Equal(Utc("2018-07-02T05:00:00Z"), query.Start);
        Assert.Equal(Utc("2018-07-09T05:00:00Z"), query.End);
    }

    [Fact]
    public async Task Daily_read_still_resolves_each_midnight_separately_on_dst_day()
    {
        await using var f = new ApiFixture { Zone = "America/New_York" };
        Assert.IsType<OkObjectResult>(await f.ReadAsync(date: new DateTime(2026, 3, 8)));
        var query = Assert.Single(f.Reads);
        Assert.Equal(Utc("2026-03-08T05:00:00Z"), query.Start);
        Assert.Equal(Utc("2026-03-09T04:00:00Z"), query.End);
        Assert.Equal(TimeSpan.FromHours(23), query.End - query.Start);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("2026-10-05", null)]
    [InlineData(null, "2026-10-12")]
    [InlineData("bad", "2026-10-12")]
    [InlineData("2026-02-30", "2026-03-01")]
    [InlineData("2026-10-05", "bad")]
    [InlineData("2026-10-05T00:00:00Z", "2026-10-12")]
    [InlineData("2026-10-5", "2026-10-12")]
    [InlineData("2026-10-05", "2026-10-05")]
    [InlineData("2026-10-06", "2026-10-05")]
    [InlineData("2026-10-05", "2026-10-13")]
    public async Task Invalid_missing_empty_negative_or_long_range_is_clear_400_before_dependencies(string? from, string? to)
    {
        await using var f = new ApiFixture();
        var result = Assert.IsType<BadRequestObjectResult>(await f.ReadAsync(from: from, to: to));
        using var error = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.Equal("Validation.Error", error.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.RootElement.GetProperty("message").GetString()));
        f.Profiles.VerifyNoOtherCalls(); f.Locations.VerifyNoOtherCalls(); f.Repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("locationId=all&date=bad")]
    [InlineData("locationId=all&date=2026-10-05&from=2026-10-05&to=2026-10-12")]
    [InlineData("locationId=all&date=&from=2026-10-05&to=2026-10-12")]
    [InlineData("locationId=all&from=&to=2026-10-12")]
    [InlineData("locationId=all&from=2026-10-05&from=2026-10-06&to=2026-10-12")]
    [InlineData("locationId=location-a&locationId=all&from=2026-10-05&to=2026-10-12")]
    public async Task Real_http_binding_rejects_invalid_dates_and_incompatible_or_repeated_query_parameters(string query)
    {
        await using var f = new ApiFixture();
        var client = await f.StartAsync();
        using var response = await client.GetAsync("/api/reservations?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(f.Reads);
        f.Profiles.VerifyNoOtherCalls(); f.Locations.VerifyNoOtherCalls(); f.Engine.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Location_selection_must_be_explicit(string? location)
    {
        await using var f = new ApiFixture();
        Assert.IsType<BadRequestObjectResult>(await f.ReadAsync(location, from: "2026-10-05", to: "2026-10-12"));
        f.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Foreign_location_is_not_authorized_as_empty_or_aggregate_results()
    {
        await using var f = new ApiFixture();
        Assert.IsType<NotFoundObjectResult>(await f.ReadAsync("foreign-location", from: "2026-10-05", to: "2026-10-12"));
        f.Locations.Verify(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>()), Times.Once);
        f.Repository.VerifyNoOtherCalls(); f.Profiles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Concrete_location_is_validated_in_authenticated_workspace_and_passed_unchanged()
    {
        await using var f = new ApiFixture();
        Assert.IsType<OkObjectResult>(await f.ReadAsync("location-b", from: "2026-10-05", to: "2026-10-12"));
        Assert.Equal("location-b", Assert.Single(f.Reads).Location);
        f.Locations.Verify(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task All_locations_never_bypasses_license_read_capability_or_authenticated_workspace(bool licensed, bool canRead, bool missingWorkspace)
    {
        await using var f = new ApiFixture(missingWorkspace ? Guid.Empty : Guid.NewGuid()) { Licensed = licensed, CanRead = canRead };
        var result = Assert.IsType<ObjectResult>(await f.ReadAsync(from: "2026-10-05", to: "2026-10-12"));
        Assert.Equal(403, result.StatusCode);
        f.Repository.VerifyNoOtherCalls(); f.Profiles.VerifyNoOtherCalls(); f.Locations.VerifyNoOtherCalls(); f.Engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancellation_is_respected_and_forwarded_to_every_dependency()
    {
        await using var f = new ApiFixture();
        using var cancellation = new CancellationTokenSource();
        Assert.IsType<OkObjectResult>(await f.ReadAsync("location-a", from: "2026-10-05", to: "2026-10-12", ct: cancellation.Token));
        Assert.Equal(cancellation.Token, Assert.Single(f.Reads).Token);
        f.Profiles.Verify(p => p.GetProfileAsync(f.Workspace, cancellation.Token), Times.Once);
        f.Locations.Verify(p => p.GetLocationsAsync(f.Workspace, cancellation.Token), Times.Once);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.ReadAsync(from: "2026-10-05", to: "2026-10-12", ct: cancellation.Token));
        Assert.Single(f.Reads);
    }

    private static DateTime Utc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Reservation Row(Guid workspace, DateTime start, ReservationStatus state)
    {
        var row = Reservation.Create(workspace, "location-a", "service", "customer", "Customer", start, start.AddMinutes(30));
        // Simulate persisted legacy states, including Pending, without changing domain transitions.
        typeof(Reservation).GetProperty(nameof(Reservation.Status))!.SetValue(row, state);
        return row;
    }

    private sealed record ReadCall(Guid Workspace, string? Location, DateTime Start, DateTime End, CancellationToken Token);

    private sealed class ApiFixture : IAsyncDisposable
    {
        public Guid Workspace { get; }
        public bool Licensed { get; init; } = true;
        public bool CanRead { get; init; } = true;
        public string? Zone { get; init; } = "America/Lima";
        public List<Reservation> Rows { get; } = new();
        public List<ReadCall> Reads { get; } = new();
        public Mock<IReservationEngine> Engine { get; } = new(MockBehavior.Strict);
        public Mock<IReservationRepository> Repository { get; } = new(MockBehavior.Strict);
        public Mock<IBusinessProfileRepository> Profiles { get; } = new(MockBehavior.Strict);
        public Mock<ILocationRepository> Locations { get; } = new(MockBehavior.Strict);
        private readonly Mock<IWorkspaceContext> _workspace = new();
        private readonly Mock<IEntitlementService> _entitlements = new(MockBehavior.Strict);
        private readonly ReservationsController _controller;
        private WebApplication? _app;
        private HttpClient? _client;

        public ApiFixture(Guid? workspace = null)
        {
            Workspace = workspace ?? Guid.NewGuid();
            _workspace.SetupGet(w => w.CurrentWorkspaceId).Returns(() => Workspace);
            _entitlements.Setup(e => e.GetAvailableModuleCodesAsync(Workspace, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Licensed ? new[] { "RESERVATIONS" } : Array.Empty<string>());
            _entitlements.Setup(e => e.HasCapabilityAccessAsync(Workspace, "RESERVATIONS", "READ", It.IsAny<CancellationToken>())).ReturnsAsync(() => CanRead);
            Profiles.Setup(p => p.GetProfileAsync(Workspace, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Zone == null ? null : new BusinessProfileDto("Business", "", "", "", "", Zone));
            Locations.Setup(l => l.GetLocationsAsync(Workspace, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { new LocationDto("location-a", "A", "Address A", null, null, true), new LocationDto("location-b", "B", "Address B", null, null, false) });
            Repository.Setup(r => r.GetReservationsForDateAsync(Workspace, It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, string?, DateTime, DateTime, CancellationToken>((w, l, start, end, ct) => Reads.Add(new(w, l, start, end, ct)))
                .ReturnsAsync(() => Rows);
            _controller = new(Engine.Object, Repository.Object, _workspace.Object, _entitlements.Object)
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        }

        public Task<IActionResult> ReadAsync(string? location = "all", DateTime? date = null, string? from = null, string? to = null, CancellationToken ct = default) =>
            _controller.GetReservations(location, date, Profiles.Object, Locations.Object, ct, from, to);

        public async Task<HttpClient> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath(), ApplicationName = typeof(ReservationsController).Assembly.GetName().Name });
            builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(_workspace.Object); builder.Services.AddSingleton(_entitlements.Object);
            builder.Services.AddSingleton(Repository.Object); builder.Services.AddSingleton(Engine.Object);
            builder.Services.AddSingleton(Profiles.Object); builder.Services.AddSingleton(Locations.Object);
            // Test transport only: no production Program, Firebase authentication, Firestore or PostgreSQL connection.
            builder.Services.AddAuthorization(options => options.AddPolicy("WorkspaceMember", policy => policy.RequireAssertion(_ => true)));
            builder.Services.AddControllers().AddApplicationPart(typeof(ReservationsController).Assembly)
                .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new ReservationsOnly()));
            _app = builder.Build(); _app.UseAuthorization(); _app.MapControllers();
            await _app.StartAsync();
            var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            _client = new HttpClient { BaseAddress = new Uri(address) };
            return _client;
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        }
    }

    private sealed class ReservationsOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.Where(c => c.AsType() != typeof(ReservationsController)).ToArray()) feature.Controllers.Remove(controller);
        }
    }
}
