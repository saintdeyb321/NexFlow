using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Gateways;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class EvolutionFetchTests
{
    [Fact]
    public async Task Filtered_404_then_empty_general_listing_confirms_absence_without_creating_on_status()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;

        var status = await f.Service.GetStatusAsync(f.Workspace.Id, true, default);

        Assert.Equal("DISCONNECTED", status.Status);
        Assert.False(status.IsLinked);
        Assert.True(status.CanConnect);
        Assert.Equal(new[] { $"?instanceName={Uri.EscapeDataString(f.Transport.Name)}", "" }, f.Transport.FetchQueries);
        AssertReadOnlyFetches(f);
    }

    [Fact]
    public async Task Other_businesses_remain_untouched_and_only_explicit_connect_creates_one_instance_after_confirmed_absence()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;
        AddOtherBusinesses(f);
        var othersBefore = JsonSerializer.Serialize(f.Transport.OtherInstances);

        var result = await f.Service.ConnectAsync(f.Workspace.Id, default);

        Assert.Equal("QR_AVAILABLE", result.Status);
        Assert.False(result.IsLinked);
        Assert.Equal("/instance/fetchInstances", f.Transport.Calls[0].Path);
        Assert.Equal("/instance/fetchInstances", f.Transport.Calls[1].Path);
        Assert.Equal("/instance/create", f.Transport.Calls[2].Path);
        Assert.Equal("", f.Transport.FetchQueries[1]);
        var create = Assert.Single(f.Transport.Calls, c => c.Path == "/instance/create");
        using var payload = JsonDocument.Parse(create.Body);
        Assert.Equal(f.Workspace.EvolutionInstanceName, payload.RootElement.GetProperty("instanceName").GetString());
        Assert.Equal(othersBefore, JsonSerializer.Serialize(f.Transport.OtherInstances));
        Assert.All(f.Transport.Calls, c => Assert.True(c.Path is "/instance/fetchInstances" or "/instance/create"
            || c.Path.EndsWith($"/{f.Transport.Name}", StringComparison.Ordinal)));

        var callsBeforeRepeat = f.Transport.Calls.Count;
        Assert.Equal(result.QrBase64, (await f.Service.ConnectAsync(f.Workspace.Id, default)).QrBase64);
        Assert.Equal(callsBeforeRepeat, f.Transport.Calls.Count);
    }

    [Theory]
    [InlineData(false, "open", "CONNECTED")]
    [InlineData(true, "open", "CONNECTED")]
    [InlineData(false, "close", "RECONNECTING")]
    [InlineData(true, "close", "RECONNECTING")]
    public async Task General_listing_exact_match_preserves_existing_session_in_current_and_legacy_formats(bool legacy, string state, string expected)
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;
        f.Transport.Exists = true; f.Transport.LegacyFormat = legacy;
        f.Transport.State = state; f.Transport.Owner = "existing-owner";
        AddOtherBusinesses(f);

        var result = await f.Service.ConnectAsync(f.Workspace.Id, default);

        Assert.Equal(expected, result.Status);
        Assert.True(result.IsLinked);
        Assert.False(result.CanConnect);
        Assert.Null(result.QrBase64);
        Assert.Equal(state, f.Transport.State);
        Assert.Equal("existing-owner", f.Transport.Owner);
        Assert.True((await f.State()).IsLinked);
        AssertReadOnlyFetches(f);
    }

    [Fact]
    public async Task Fallback_preserves_owner_and_explicit_logout_reason_metadata()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;
        f.Transport.Exists = true; f.Transport.LegacyFormat = true;
        f.Transport.State = "close"; f.Transport.Owner = "previous-owner";
        f.Transport.DisconnectionReasonCode = 401; f.Transport.RejectClosedLogout = true;

        Assert.True((await f.Service.GetStatusAsync(f.Workspace.Id, true, default)).IsLinked);
        Assert.False((await f.Service.DisconnectAsync(f.Workspace.Id, true, default)).IsLinked);
        Assert.Equal("previous-owner", (await f.State()).LoggedOutOwner);
        Assert.DoesNotContain(f.Transport.Calls, c => c.Path == "/instance/create" || c.Path.StartsWith("/instance/connect/"));
    }

    [Fact]
    public async Task Case_variants_and_prefix_matches_in_general_listing_do_not_claim_requested_workspace()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;
        f.Transport.OtherInstances.Add(new { name = f.Transport.Name.ToUpperInvariant(), connectionStatus = "open" });
        f.Transport.OtherInstances.Add(new { instance = new { instanceName = f.Transport.Name + "-other", status = "open" } });

        var result = await f.Service.GetStatusAsync(f.Workspace.Id, true, default);

        Assert.Equal("DISCONNECTED", result.Status);
        Assert.False(result.IsLinked);
        AssertReadOnlyFetches(f);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.InternalServerError)]
    public async Task Authentication_and_server_errors_never_authorize_creation(bool general, HttpStatusCode status)
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = general ? HttpStatusCode.NotFound : status;
        f.Transport.GeneralFetchStatus = status;

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));

        Assert.Equal(status, failure.StatusCode);
        Assert.Equal(general ? 2 : 1, f.Transport.FetchQueries.Count);
        AssertReadOnlyFetches(f);
        Assert.Null((await f.State()).IsLinked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[42]")]
    [InlineData("[{}]")]
    [InlineData("[{\"instance\":null}]")]
    [InlineData("[{\"name\":42}]")]
    [InlineData("[{\"name\":\"\"}]")]
    [InlineData("[{\"name\":null}]")]
    [InlineData("[{\"name\":\"one\",\"instanceName\":\"two\"}]")]
    public async Task Invalid_general_json_or_identity_cannot_be_used_as_absence_proof(string json)
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;
        f.Transport.GeneralFetchJson = json;

        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));

        AssertReadOnlyFetches(f);
        Assert.Null((await f.State()).IsLinked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_exact_matches_are_ambiguous_even_across_recognized_formats(bool legacy)
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;
        f.Transport.Exists = true; f.Transport.LegacyFormat = legacy;
        f.Transport.OtherInstances.Add(new { name = f.Transport.Name, connectionStatus = "open" });

        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));

        AssertReadOnlyFetches(f);
    }

    [Fact]
    public async Task General_network_failure_requires_successful_reconciliation_before_retry_can_create()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;
        f.Transport.FailGeneralFetch = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));
        AssertReadOnlyFetches(f);

        f.Transport.FailGeneralFetch = false;
        Assert.Equal("QR_AVAILABLE", (await f.Service.ConnectAsync(f.Workspace.Id, default)).Status);
        Assert.Single(f.Transport.Calls, c => c.Path == "/instance/create");
    }

    [Fact]
    public async Task Previously_linked_workspace_stays_reserved_when_listing_has_only_other_instances_or_fails()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        await f.Service.ObserveConnectionAsync(f.Workspace.Id, "open", default);
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;
        AddOtherBusinesses(f);

        var missing = await f.Service.ConnectAsync(f.Workspace.Id, default);
        Assert.True(missing.IsLinked);
        Assert.False(missing.CanConnect);
        Assert.Null(missing.QrBase64);

        f.Transport.FailGeneralFetch = true;
        var outage = await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        Assert.Equal("UNAVAILABLE", outage.Status);
        Assert.True(outage.IsLinked);
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));
        Assert.True((await f.State()).IsLinked);
        AssertReadOnlyFetches(f);
    }

    [Fact]
    public async Task Double_connect_while_general_listing_is_pending_accepts_only_one_creator()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FilteredFetchStatus = HttpStatusCode.NotFound;
        f.Transport.PauseGeneralFetch = true;
        f.Transport.FirstFetchEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.ContinueFirstFetch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AddOtherBusinesses(f);
        var first = f.Service.ConnectAsync(f.Workspace.Id, default);
        await f.Transport.FirstFetchEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var otherDb = f.NewContext();
        using var otherHttp = new HttpClient(f.Transport, false);
        var other = new EvolutionConnectionService(otherHttp, f.Config, new WhatsAppConnectionRepository(otherDb), f.Time, NullLogger<EvolutionConnectionService>.Instance);
        try
        {
            AssertReadOnlyFetches(f);
            Assert.Equal(2, f.Transport.FetchQueries.Count);
            await Assert.ThrowsAsync<ConcurrencyException>(() => other.ConnectAsync(f.Workspace.Id, default));
        }
        finally { f.Transport.ContinueFirstFetch.SetResult(); }

        Assert.Equal("QR_AVAILABLE", (await first).Status);
        Assert.Single(f.Transport.Calls, c => c.Path == "/instance/create");
        Assert.Single(f.Transport.Calls, c => c.Path.StartsWith("/instance/connect/"));
    }

    private static void AddOtherBusinesses(EvolutionFixture f)
    {
        f.Transport.OtherInstances.Add(new { name = "other-configuration-one", connectionStatus = "close", ownerJid = (string?)null });
        f.Transport.OtherInstances.Add(new { instance = new { instanceName = "other-configuration-two", status = "close", owner = "other-owner" } });
    }

    private static void AssertReadOnlyFetches(EvolutionFixture f)
    {
        Assert.NotEmpty(f.Transport.Calls);
        Assert.All(f.Transport.Calls, c => { Assert.Equal("GET", c.Method); Assert.Equal("/instance/fetchInstances", c.Path); });
    }
}
