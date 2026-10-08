using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Gateways;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class EvolutionConnectionTests
{
    [Fact]
    public async Task Provisioning_assigns_stable_normalized_unique_identity_without_provider_dependency()
    {
        var workspaceRepo = new Mock<IWorkspaceRepository>();
        Workspace? created = null;
        workspaceRepo.Setup(r => r.Add(It.IsAny<Workspace>())).Callback<Workspace>(w => created = w);
        var modules = new Mock<IModuleRepository>();
        modules.Setup(r => r.GetByCodesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync([Module.Create("CONVERSATIONS", "Conversations")]);
        var handler = new ProvisionClientCommandHandler(Mock.Of<IUserRepository>(), workspaceRepo.Object, Mock.Of<IMembershipRepository>(),
            Mock.Of<ILicenseRepository>(), Mock.Of<ITemplateRepository>(), modules.Object, Mock.Of<IAuditLogRepository>(), Mock.Of<IUnitOfWork>(), new EvolutionFixture.Clock());
        var result = await handler.Handle(new("owner@example.test", "Owner", "Example", "Commercial Name", null, DateTime.UtcNow.AddDays(30), ["CONVERSATIONS"]), default);
        Assert.True(result.IsSuccess);
        Assert.NotNull(created);
        Assert.Equal($"nexflow{result.Value:N}", created.EvolutionInstanceName);
        var original = created.EvolutionInstanceName;
        created.Rename("A completely different commercial name");
        Assert.Equal(original, created.EvolutionInstanceName);
        Assert.NotEqual(original, Workspace.Create("Commercial Name").EvolutionInstanceName);
        Assert.Throws<ConcurrencyException>(() => created.LinkEvolutionInstance("another-business"));
    }

    [Fact]
    public async Task Status_read_never_creates_or_connects_instance()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        Assert.Equal("DISCONNECTED", (await f.Service.GetStatusAsync(f.Workspace.Id, false, default)).Status);
        await f.Service.GetStatusAsync(f.Workspace.Id, false, default);
        Assert.Single(f.Transport.Calls);
        Assert.All(f.Transport.Calls, c => Assert.Equal("/instance/fetchInstances", c.Path));
    }

    [Fact]
    public async Task First_explicit_connection_creates_one_instance_configures_webhook_then_returns_qr()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var result = await f.Service.ConnectAsync(f.Workspace.Id, default);
        Assert.Equal("QR_AVAILABLE", result.Status);
        Assert.NotNull(result.QrBase64);
        Assert.False(result.IsLinked);
        Assert.Equal(f.Time.UtcNow.AddSeconds(30), result.QrExpiresAt);
        var webhook = Assert.Single(f.Transport.Calls, c => c.Path.StartsWith("/webhook/set/"));
        Assert.Contains("CONNECTION_UPDATE", webhook.Body);
        Assert.Contains(EvolutionConnectionService.InstanceWebhookKey("unit-test-webhook", f.Transport.Name), webhook.Body);
        Assert.DoesNotContain("unit-test-key", System.Text.Json.JsonSerializer.Serialize(result));
        Assert.Single(f.Transport.Calls, c => c.Path == "/instance/create");
        var repeated = await f.Service.ConnectAsync(f.Workspace.Id, default);
        Assert.Equal(result.QrBase64, repeated.QrBase64);
        Assert.Single(f.Transport.Calls, c => c.Path.StartsWith("/instance/connect/"));
    }

    [Fact]
    public async Task Expired_qr_is_not_exposed_and_explicit_retry_reuses_instance()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var qr = await f.Service.ConnectAsync(f.Workspace.Id, default);
        f.Time.UtcNow = f.Time.UtcNow.AddSeconds(31);
        var expired = await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        Assert.Equal("QR_EXPIRED", expired.Status);
        Assert.Null(expired.QrBase64);
        var renewed = await f.Service.ConnectAsync(f.Workspace.Id, default);
        Assert.NotEqual(qr.QrBase64, renewed.QrBase64);
        Assert.Single(f.Transport.Calls, c => c.Path == "/instance/create");
    }

    [Fact]
    public async Task Successful_link_removes_qr_and_blocks_new_pairing()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        await f.Service.ConnectAsync(f.Workspace.Id, default);
        f.Transport.State = "open"; f.Transport.Owner = "51999999999@s.whatsapp.net";
        await f.Service.ObserveConnectionAsync(f.Workspace.Id, "open", default);
        var connected = await f.Service.GetStatusAsync(f.Workspace.Id, false, default);
        Assert.True(connected.IsLinked);
        Assert.False(connected.CanConnect);
        Assert.Null(connected.QrBase64);
        Assert.Equal("CONNECTED", (await f.Service.ConnectAsync(f.Workspace.Id, default)).Status);
        Assert.Single(f.Transport.Calls, c => c.Path.StartsWith("/instance/connect/"));
    }

    [Fact]
    public async Task Concurrent_clicks_from_two_backend_contexts_accept_one_operation()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.FirstFetchEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.ContinueFirstFetch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = f.Service.ConnectAsync(f.Workspace.Id, default);
        await f.Transport.FirstFetchEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var otherDb = f.NewContext();
        using var otherHttp = new HttpClient(f.Transport, false);
        var other = new EvolutionConnectionService(otherHttp, f.Config, new WhatsAppConnectionRepository(otherDb), f.Time, NullLogger<EvolutionConnectionService>.Instance);
        try { await Assert.ThrowsAsync<ConcurrencyException>(() => other.ConnectAsync(f.Workspace.Id, default)); }
        finally { f.Transport.ContinueFirstFetch.SetResult(); }
        Assert.Equal("QR_AVAILABLE", (await first).Status);
        Assert.Single(f.Transport.Calls, c => c.Path == "/instance/create");
    }

    [Fact]
    public async Task Temporary_disconnection_and_network_failure_preserve_link_and_never_generate_another_qr()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.State = "open"; f.Transport.Owner = "51999999999@s.whatsapp.net";
        await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        f.Transport.State = "close";
        await f.Service.ObserveConnectionAsync(f.Workspace.Id, "close", default);
        Assert.Equal("RECONNECTING", (await f.Service.ConnectAsync(f.Workspace.Id, default)).Status);
        f.Transport.FailRequests = true;
        var outage = await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        Assert.Equal("UNAVAILABLE", outage.Status);
        Assert.True(outage.IsLinked);
        Assert.False(outage.CanConnect);
        Assert.DoesNotContain(f.Transport.Calls, c => c.Path == "/instance/create" || c.Path.StartsWith("/instance/connect/") || c.Path.StartsWith("/instance/logout/"));
        f.Transport.FailRequests = false; f.Transport.State = "open";
        Assert.Equal("CONNECTED", (await f.Service.GetStatusAsync(f.Workspace.Id, true, default)).Status);
    }

    [Fact]
    public async Task Missing_remote_instance_does_not_release_a_previously_linked_session()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.State = "open";
        await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        f.Transport.Exists = false;
        var status = await f.Service.ConnectAsync(f.Workspace.Id, default);
        Assert.True(status.IsLinked);
        Assert.Null(status.QrBase64);
        Assert.DoesNotContain(f.Transport.Calls, c => c.Path == "/instance/create");
    }

    [Fact]
    public async Task Logout_requires_confirmation_and_preserves_instance_and_historical_owner()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.State = "open"; f.Transport.Owner = "51999999999@s.whatsapp.net";
        await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        var count = f.Transport.Calls.Count;
        await Assert.ThrowsAsync<DomainException>(() => f.Service.DisconnectAsync(f.Workspace.Id, false, default));
        Assert.Equal(count, f.Transport.Calls.Count);
        var result = await f.Service.DisconnectAsync(f.Workspace.Id, true, default);
        Assert.False(result.IsLinked);
        Assert.True(result.CanConnect);
        Assert.Equal("DISCONNECTED", (await f.Service.GetStatusAsync(f.Workspace.Id, true, default)).Status);
        var afterLogout = f.Transport.Calls.Count;
        await f.Service.DisconnectAsync(f.Workspace.Id, true, default);
        Assert.Equal(afterLogout, f.Transport.Calls.Count);
        Assert.Equal("QR_AVAILABLE", (await f.Service.ConnectAsync(f.Workspace.Id, default)).Status);
        Assert.DoesNotContain(f.Transport.Calls, c => c.Path.StartsWith("/instance/delete/"));
        Assert.Equal(f.Workspace.EvolutionInstanceName, (await f.Repository.GetAsync(f.Workspace.Id, default)).InstanceName);
    }

    [Fact]
    public async Task Failed_logout_blocks_pairing_until_explicit_retry_confirms_close()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.State = "open";
        await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        f.Transport.FailLogout = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.DisconnectAsync(f.Workspace.Id, true, default));
        var pending = await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        Assert.Equal("DISCONNECT_PENDING", pending.Status);
        Assert.True(pending.IsLinked);
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));
        f.Transport.FailLogout = false;
        Assert.False((await f.Service.DisconnectAsync(f.Workspace.Id, true, default)).IsLinked);
    }

    [Fact]
    public async Task Lost_create_acknowledgement_is_recovered_without_duplicate_instance()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.LoseCreateAcknowledgement = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));
        Assert.Equal("QR_AVAILABLE", (await f.Service.ConnectAsync(f.Workspace.Id, default)).Status);
        Assert.Single(f.Transport.Calls, c => c.Path == "/instance/create");
    }

    [Fact]
    public async Task Legacy_session_is_detected_from_metadata_even_when_offline()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.State = "close"; f.Transport.Owner = "old-number@s.whatsapp.net"; f.Transport.LegacyFormat = true;
        Assert.True((await f.Service.ConnectAsync(f.Workspace.Id, default)).IsLinked);
        Assert.DoesNotContain(f.Transport.Calls, c => c.Path == "/instance/create" || c.Path.StartsWith("/instance/connect/"));
    }

    [Fact]
    public async Task Unknown_legacy_session_metadata_fails_closed()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.IncludeOwner = false;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));
        Assert.DoesNotContain(f.Transport.Calls, c => c.Path == "/instance/create" || c.Path.StartsWith("/instance/connect/"));
    }

    [Fact]
    public async Task Expired_lease_cannot_complete_or_release_a_newer_operation()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var old = await f.Repository.AcquireAsync(f.Workspace.Id, false, f.Time.UtcNow, f.Time.UtcNow.AddSeconds(1), default);
        f.Time.UtcNow = f.Time.UtcNow.AddSeconds(2);
        var current = await f.Repository.AcquireAsync(f.Workspace.Id, false, f.Time.UtcNow, f.Time.UtcNow.AddMinutes(2), default);
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Repository.SaveObservationAsync(f.Workspace.Id, old, new("QR_AVAILABLE", false, "old-qr", f.Time.UtcNow.AddSeconds(30)), f.Time.UtcNow, default));
        await f.Repository.ReleaseAsync(f.Workspace.Id, old, default);
        Assert.Equal(current, (await f.State()).OperationId);
    }

    [Fact]
    public async Task Stale_observation_cannot_overwrite_confirmed_logout()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var before = await f.State();
        var op = await f.Repository.AcquireAsync(f.Workspace.Id, true, f.Time.UtcNow, f.Time.UtcNow.AddMinutes(2), default);
        await f.Repository.ConfirmLogoutAsync(f.Workspace.Id, op, "old-owner", f.Time.UtcNow, default);
        await f.Repository.SaveObservationAsync(f.Workspace.Id, null, new("CONNECTED", true), f.Time.UtcNow, default, before.PersistenceVersion);
        Assert.False((await f.State()).IsLinked);
    }

    [Fact]
    public async Task Explicit_logout_can_confirm_an_already_logged_out_provider_session()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.State = "open"; f.Transport.Owner = "old-owner@s.whatsapp.net";
        await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        f.Transport.State = "close"; f.Transport.DisconnectionReasonCode = 401; f.Transport.RejectClosedLogout = true;
        Assert.True((await f.Service.GetStatusAsync(f.Workspace.Id, true, default)).IsLinked);
        Assert.False((await f.Service.DisconnectAsync(f.Workspace.Id, true, default)).IsLinked);
        Assert.Equal("QR_AVAILABLE", (await f.Service.ConnectAsync(f.Workspace.Id, default)).Status);
    }

    [Fact]
    public async Task Closed_provider_without_logout_proof_cannot_release_a_linked_session()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.State = "open"; f.Transport.Owner = "old-owner@s.whatsapp.net";
        await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        f.Transport.State = "close"; f.Transport.RejectClosedLogout = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.DisconnectAsync(f.Workspace.Id, true, default));
        Assert.True((await f.State()).IsLinked);
        Assert.True((await f.State()).LogoutPending);
    }

    [Fact]
    public async Task Delayed_open_webhook_after_confirmed_logout_does_not_revive_previous_session()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.State = "open";
        await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        await f.Service.DisconnectAsync(f.Workspace.Id, true, default);
        await f.Service.ObserveConnectionAsync(f.Workspace.Id, "open", default);
        Assert.False((await f.State()).IsLinked);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.NotFound)]
    [InlineData(System.Net.HttpStatusCode.BadRequest)]
    public async Task Missing_instance_without_logout_acknowledgement_preserves_link(System.Net.HttpStatusCode statusCode)
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Transport.Exists = true; f.Transport.State = "open";
        await f.Service.GetStatusAsync(f.Workspace.Id, true, default);
        f.Transport.Exists = false; f.Transport.RejectMissingLogout = true; f.Transport.MissingLogoutStatus = statusCode;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.DisconnectAsync(f.Workspace.Id, true, default));
        Assert.True((await f.State()).IsLinked);
        Assert.True((await f.State()).LogoutPending);
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));
    }
}
