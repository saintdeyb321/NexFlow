using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.API.Controllers.Business;
using NexFlow.API.Controllers.Webhooks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Gateways;
using NexFlow.Tests.Fakes;
using Xunit;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;

namespace NexFlow.Tests;

public sealed class EvolutionSecurityTests
{
    [Fact]
    public async Task Rejected_foreign_workspace_message_cannot_reassign_legacy_instance_identity()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        await f.Db.Workspaces.Where(w => w.Id == f.Workspace.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, "Existing-Business"));
        var resolver = new DefaultInstanceResolver(f.Db);
        var entitlements = new Mock<IEntitlementService>(MockBehavior.Strict);
        var guard = new IncomingMessageGuard(resolver, entitlements.Object, NullLogger<IncomingMessageGuard>.Instance);
        var command = new ProcessIncomingMessageCommand("existingbusiness", "51999999999", "Customer", "Hello", "message", false, Guid.NewGuid(), f.Time.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.CheckMessageAsync(command, default));
        Assert.Equal("Existing-Business", await resolver.GetInstanceNameAsync(f.Workspace.Id, default));
        entitlements.VerifyNoOtherCalls();
        Assert.Empty(f.Transport.Calls);
    }

    [Fact]
    public async Task Only_authenticated_provider_webhook_can_confirm_a_legacy_alias()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        await f.Db.Workspaces.Where(w => w.Id == f.Workspace.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, "Existing-Business"));
        var resolver = new DefaultInstanceResolver(f.Db);
        var payload = new EvolutionWebhookController.EvolutionWebhookPayload { Instance = "existingbusiness", Event = "connection.update", Data = new() { State = "open" } };
        Assert.IsType<UnauthorizedObjectResult>(await Webhook("invalid-key").ReceiveMessage(payload, f.Config, Mock.Of<IInboundMessageRepository>(), resolver, f.Service));
        Assert.Equal("Existing-Business", await resolver.GetInstanceNameAsync(f.Workspace.Id, default));
        var key = EvolutionConnectionService.InstanceWebhookKey("unit-test-webhook", payload.Instance);
        Assert.IsType<OkResult>(await Webhook(key).ReceiveMessage(payload, f.Config, Mock.Of<IInboundMessageRepository>(), resolver, f.Service));
        Assert.Equal(payload.Instance, await resolver.GetInstanceNameAsync(f.Workspace.Id, default));
        Assert.True((await f.State()).IsLinked);
        Assert.Empty(f.Transport.Calls);
    }

    [Theory]
    [InlineData("text", "Legacy#Business")]
    [InlineData("document", "Legacy?Business")]
    [InlineData("image", "Legacy#Business")]
    public async Task Outbound_legacy_name_remains_one_url_parameter_and_cannot_route_to_another_instance(string kind, string name)
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var other = Workspace.Create("Other business"); f.Db.Workspaces.Add(other); await f.Db.SaveChangesAsync();
        await f.Db.Workspaces.Where(w => w.Id == f.Workspace.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, name));
        await f.Db.Workspaces.Where(w => w.Id == other.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, "Legacy"));
        using var transport = new MessageTransport(); using var http = new HttpClient(transport);
        var gateway = new EvolutionMessageGateway(http, f.Config, NullLogger<EvolutionMessageGateway>.Instance, new DefaultInstanceResolver(f.Db));
        var result = kind switch
        {
            "text" => await gateway.SendTextAsync(f.Workspace.Id, "51999999999", "Hello", "message", _ => Task.CompletedTask, default),
            "document" => await gateway.SendDocumentAsync(f.Workspace.Id, "51999999999", "https://media.example.test/catalog.pdf", "catalog.pdf", "Catalog", "message", _ => Task.CompletedTask, default),
            _ => await gateway.SendImageAsync(f.Workspace.Id, "51999999999", "https://media.example.test/image.png", "Image", "message", default)
        };
        Assert.Equal("provider-message", result);
        Assert.NotNull(transport.Uri);
        Assert.Equal($"/message/{(kind == "text" ? "sendText" : "sendMedia")}/{Uri.EscapeDataString(name)}", transport.Uri.AbsolutePath);
        Assert.Empty(transport.Uri.Query);
        Assert.Empty(transport.Uri.Fragment);
    }

    private sealed class MessageTransport : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"key\":{\"id\":\"provider-message\"}}", System.Text.Encoding.UTF8, "application/json") });
        }
    }

    [Theory]
    [InlineData("false")]
    [InlineData("invalid")]
    public async Task Legacy_key_retirement_rejects_shared_key_but_preserves_instance_authentication(string setting)
    {
        await using var f = await EvolutionFixture.CreateAsync();
        f.Config["Evolution:AllowLegacyWebhookKey"] = setting;
        var payload = new EvolutionWebhookController.EvolutionWebhookPayload { Instance = f.Workspace.EvolutionInstanceName!, Event = "connection.update", Data = new() { State = "open" } };
        var resolver = new DefaultInstanceResolver(f.Db);
        Assert.IsType<UnauthorizedObjectResult>(await Webhook("unit-test-webhook").ReceiveMessage(payload, f.Config, Mock.Of<IInboundMessageRepository>(), resolver, f.Service));
        Assert.False(await f.Db.WhatsAppConnections.AnyAsync());
        var key = EvolutionConnectionService.InstanceWebhookKey("unit-test-webhook", payload.Instance);
        Assert.IsType<OkResult>(await Webhook(key).ReceiveMessage(payload, f.Config, Mock.Of<IInboundMessageRepository>(), resolver, f.Service));
        Assert.True((await f.State()).IsLinked);
        Assert.Empty(f.Transport.Calls);
    }

    [Fact]
    public async Task Conflicting_instance_in_message_event_never_reaches_durable_inbound_repository()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var inbound = new Mock<IInboundMessageRepository>(MockBehavior.Strict);
        var payload = new EvolutionWebhookController.EvolutionWebhookPayload { Instance = f.Workspace.EvolutionInstanceName!, Event = "messages.upsert", Data = new() { Instance = "another-workspace", Key = new() { Id = "message", RemoteJid = "51999999999@s.whatsapp.net" }, Message = new() { Conversation = "Hello" } } };
        Assert.IsType<BadRequestObjectResult>(await Webhook("unit-test-webhook").ReceiveMessage(payload, f.Config, inbound.Object, new DefaultInstanceResolver(f.Db), f.Service));
        inbound.VerifyNoOtherCalls();
        Assert.Empty(f.Transport.Calls);
    }

    [Fact]
    public async Task Workspace_without_permissions_cannot_read_connect_or_logout()
    {
        var workspace = Guid.NewGuid();
        var context = new Mock<IWorkspaceContext>(); context.SetupGet(c => c.CurrentWorkspaceId).Returns(workspace);
        var permissions = new Mock<IEntitlementService>();
        var provider = new Mock<IEvolutionConnectionService>(MockBehavior.Strict);
        var controller = Controller(context.Object, permissions.Object);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.GetWhatsAppStatus(false, provider.Object, default)).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.ConnectWhatsApp(provider.Object, default)).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.DisconnectWhatsApp(new(true), provider.Object, default)).StatusCode);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Read_only_permissions_do_not_expose_pairing_qr()
    {
        var workspace = Guid.NewGuid();
        var context = new Mock<IWorkspaceContext>(); context.SetupGet(c => c.CurrentWorkspaceId).Returns(workspace);
        var permissions = new Mock<IEntitlementService>();
        permissions.Setup(p => p.HasCapabilityAccessAsync(workspace, "CONVERSATIONS", "READ", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var provider = new Mock<IEvolutionConnectionService>();
        provider.Setup(p => p.GetStatusAsync(workspace, false, It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppConnectionStatus("QR_AVAILABLE", false, true, false, "secret-qr"));
        var result = Assert.IsType<OkObjectResult>(await Controller(context.Object, permissions.Object).GetWhatsAppStatus(false, provider.Object, default));
        var status = Assert.IsType<WhatsAppConnectionStatus>(result.Value);
        Assert.Null(status.QrBase64);
        Assert.False(status.CanConnect);
    }

    [Fact]
    public async Task Ambiguous_legacy_alias_never_routes_to_another_business_or_calls_provider()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var other = Workspace.Create("Other business");
        f.Db.Workspaces.Add(other); await f.Db.SaveChangesAsync();
        await f.Db.Workspaces.Where(w => w.Id == f.Workspace.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, "Shared-Name"));
        await f.Db.Workspaces.Where(w => w.Id == other.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, "sharedname"));
        var resolver = new DefaultInstanceResolver(f.Db);
        Assert.Null(await resolver.ResolveInstanceAsync("sharedname", default));
        Assert.Null(await resolver.GetInstanceNameAsync(f.Workspace.Id, default));
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Service.ConnectAsync(f.Workspace.Id, default));
        Assert.Empty(f.Transport.Calls);
    }

    [Fact]
    public async Task Unique_existing_provider_name_is_preserved_without_creating_instance()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        const string original = "Existing-Business";
        await f.Db.Workspaces.Where(w => w.Id == f.Workspace.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, original));
        f.Transport.Name = original; f.Transport.Exists = true; f.Transport.State = "open";
        Assert.True((await f.Service.ConnectAsync(f.Workspace.Id, default)).IsLinked);
        Assert.Equal(original, (await f.Repository.GetAsync(f.Workspace.Id, default)).InstanceName);
        Assert.DoesNotContain(f.Transport.Calls, c => c.Path == "/instance/create");
    }

    [Fact]
    public async Task Legacy_normalized_provider_alias_repairs_outbound_and_inbound_routing_once()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        await f.Db.Workspaces.Where(w => w.Id == f.Workspace.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, "Existing-Business"));
        var resolver = new DefaultInstanceResolver(f.Db);
        Assert.Equal(f.Workspace.Id, await resolver.ResolveInstanceAsync("existingbusiness", default));
        Assert.Equal("Existing-Business", await resolver.GetInstanceNameAsync(f.Workspace.Id, default));
        Assert.Equal(f.Workspace.Id, await resolver.ResolveAuthenticatedInstanceAsync("existingbusiness", default));
        Assert.Equal("existingbusiness", await resolver.GetInstanceNameAsync(f.Workspace.Id, default));
        Assert.Equal(f.Workspace.Id, await resolver.ResolveInstanceAsync("Existing-Business", default));
        Assert.Equal("existingbusiness", await resolver.GetInstanceNameAsync(f.Workspace.Id, default));
        f.Transport.Name = "existingbusiness"; f.Transport.Exists = true; f.Transport.State = "open";
        Assert.True((await f.Service.ConnectAsync(f.Workspace.Id, default)).IsLinked);
        Assert.DoesNotContain(f.Transport.Calls, c => c.Path == "/instance/create");
    }

    [Fact]
    public async Task Status_adopts_existing_normalized_provider_alias_without_transferring_ownership()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        await f.Db.Workspaces.Where(w => w.Id == f.Workspace.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.EvolutionInstanceName, "Existing-Business"));
        f.Transport.Name = "existingbusiness"; f.Transport.Exists = true; f.Transport.State = "open";
        Assert.True((await f.Service.GetStatusAsync(f.Workspace.Id, true, default)).IsLinked);
        Assert.Equal("existingbusiness", (await f.Repository.GetAsync(f.Workspace.Id, default)).InstanceName);
        Assert.DoesNotContain(f.Transport.Calls, c => c.Path == "/instance/create");
    }

    [Fact]
    public async Task Workspace_and_connection_operations_remain_isolated()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var other = Workspace.Create("Other business"); f.Db.Workspaces.Add(other); await f.Db.SaveChangesAsync();
        var operation = await f.Repository.AcquireAsync(f.Workspace.Id, false, f.Time.UtcNow, f.Time.UtcNow.AddMinutes(2), default);
        await f.Repository.GetAsync(other.Id, default);
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Repository.SaveObservationAsync(other.Id, operation, new("CONNECTED", true), f.Time.UtcNow, default));
        Assert.NotEqual(f.Workspace.EvolutionInstanceName, (await f.Repository.GetAsync(other.Id, default)).InstanceName);
        Assert.Null((await f.Repository.GetAsync(other.Id, default)).Connection.IsLinked);
    }

    [Fact]
    public async Task Instance_specific_webhook_key_cannot_impersonate_another_workspace()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var other = Workspace.Create("Other business"); f.Db.Workspaces.Add(other); await f.Db.SaveChangesAsync();
        var controller = Webhook(EvolutionConnectionService.InstanceWebhookKey("unit-test-webhook", f.Workspace.EvolutionInstanceName!));
        var inbound = new Mock<IInboundMessageRepository>(MockBehavior.Strict);
        var payload = new EvolutionWebhookController.EvolutionWebhookPayload { Instance = other.EvolutionInstanceName!, Event = "connection.update", Data = new() { State = "open" } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.ReceiveMessage(payload, f.Config, inbound.Object, new DefaultInstanceResolver(f.Db), f.Service));
        inbound.VerifyNoOtherCalls();
        Assert.False(await f.Db.WhatsAppConnections.AnyAsync(c => c.WorkspaceId == other.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Authenticated_webhooks_route_only_to_the_instance_owner_with_legacy_key_compatibility(bool scopedKey)
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var key = scopedKey ? EvolutionConnectionService.InstanceWebhookKey("unit-test-webhook", f.Workspace.EvolutionInstanceName!) : "unit-test-webhook";
        var controller = Webhook(key);
        var payload = new EvolutionWebhookController.EvolutionWebhookPayload { Instance = f.Workspace.EvolutionInstanceName!, Event = "connection.update", Data = new() { State = "open" } };
        Assert.IsType<OkResult>(await controller.ReceiveMessage(payload, f.Config, Mock.Of<IInboundMessageRepository>(), new DefaultInstanceResolver(f.Db), f.Service));
        Assert.True((await f.State()).IsLinked);
    }

    [Fact]
    public async Task Inbound_message_uses_resolved_workspace_and_durable_repository()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var controller = Webhook(EvolutionConnectionService.InstanceWebhookKey("unit-test-webhook", f.Workspace.EvolutionInstanceName!));
        InboundMessage? saved = null;
        var inbound = new Mock<IInboundMessageRepository>();
        inbound.Setup(r => r.AddAsync(It.IsAny<InboundMessage>(), It.IsAny<CancellationToken>())).Callback<InboundMessage, CancellationToken>((m, _) => saved = m).Returns(Task.CompletedTask);
        var payload = new EvolutionWebhookController.EvolutionWebhookPayload { Instance = f.Workspace.EvolutionInstanceName!, Event = "messages.upsert", Data = new() { Key = new() { Id = "message", RemoteJid = "51999999999@s.whatsapp.net" }, Message = new() { Conversation = "Hello" } } };
        Assert.IsType<OkResult>(await controller.ReceiveMessage(payload, f.Config, inbound.Object, new DefaultInstanceResolver(f.Db), f.Service));
        Assert.NotNull(saved);
        Assert.Equal(f.Workspace.Id, saved.WorkspaceId);
        Assert.Equal(f.Workspace.EvolutionInstanceName, saved.InstanceName);
        Assert.Equal(InboundMessageStatus.Pending, saved.Status);
    }

    [Fact]
    public async Task Conflicting_instance_in_connection_event_is_rejected()
    {
        await using var f = await EvolutionFixture.CreateAsync();
        var controller = Webhook("unit-test-webhook");
        var payload = new EvolutionWebhookController.EvolutionWebhookPayload { Instance = f.Workspace.EvolutionInstanceName!, Event = "connection.update", Data = new() { Instance = "another-workspace", State = "open" } };
        Assert.IsType<BadRequestObjectResult>(await controller.ReceiveMessage(payload, f.Config, Mock.Of<IInboundMessageRepository>(), new DefaultInstanceResolver(f.Db), f.Service));
    }

    private static BusinessController Controller(IWorkspaceContext workspace, IEntitlementService entitlements) => new(
        Mock.Of<IBusinessProfileRepository>(), Mock.Of<ICatalogRepository>(), Mock.Of<IFaqRepository>(), Mock.Of<ILocationRepository>(),
        Mock.Of<IBusinessHoursRepository>(), workspace, Mock.Of<IWorkspaceRepository>(), Mock.Of<IUnitOfWork>(), entitlements);
    private static EvolutionWebhookController Webhook(string key)
    {
        var controller = new EvolutionWebhookController(NullLogger<EvolutionWebhookController>.Instance) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.Request.Headers["X-NexFlow-Webhook-Key"] = key;
        return controller;
    }
}
