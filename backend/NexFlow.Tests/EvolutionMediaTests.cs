using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Gateways;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;
using Value = Google.Cloud.Firestore.V1.Value;

namespace NexFlow.Tests;

public sealed class EvolutionMediaTests
{
    [Theory]
    [InlineData("document", "success")]
    [InlineData("image", "success")]
    [InlineData("document", "400")]
    [InlineData("image", "400")]
    [InlineData("document", "timeout")]
    [InlineData("image", "timeout")]
    [InlineData("document", "body-timeout")]
    [InlineData("image", "body-timeout")]
    [InlineData("document", "missing-id")]
    [InlineData("image", "missing-id")]
    [InlineData("document", "network")]
    [InlineData("image", "network")]
    public async Task Media_237_contract_is_flat_and_provider_outcome_is_never_fabricated(string kind, string outcome)
    {
        using var f = new Fixture(outcome);
        var started = 0;
        async Task<string> Send() => kind == "document"
            ? await f.Gateway.SendDocumentAsync(f.Workspace, "51999999999", "https://media.example.test/catalog.pdf", "catalog.pdf", "Catálogo real", "durable-id", _ => { started++; return Task.CompletedTask; }, default)
            : await f.Gateway.SendImageAsync(f.Workspace, "51999999999", "https://media.example.test/image.png?version=1", "Imagen real", "durable-id", default);
        if (outcome == "success") Assert.Equal("provider-message", await Send());
        else if (outcome == "400") Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<HttpRequestException>(Send)).StatusCode);
        else if (outcome is "timeout" or "body-timeout") await Assert.ThrowsAnyAsync<OperationCanceledException>(Send);
        else if (outcome == "network") await Assert.ThrowsAsync<HttpRequestException>(Send);
        else await Assert.ThrowsAsync<InvalidOperationException>(Send);
        var body = Assert.Single(f.Transport.Bodies);
        var expected = kind == "document"
            ? """{"number":"51999999999","mediatype":"document","mimetype":"application/pdf","media":"https://media.example.test/catalog.pdf","fileName":"catalog.pdf","caption":"Catálogo real","delay":2000}"""
            : """{"number":"51999999999","mediatype":"image","mimetype":"image/png","media":"https://media.example.test/image.png?version=1","fileName":"image.png","caption":"Imagen real","delay":1500}""";
        using var actualJson = JsonDocument.Parse(body); using var expectedJson = JsonDocument.Parse(expected);
        Assert.Equal(expectedJson.RootElement.EnumerateObject().Select(p => p.Name).Order(), actualJson.RootElement.EnumerateObject().Select(p => p.Name).Order());
        foreach (var property in expectedJson.RootElement.EnumerateObject())
        {
            Assert.Equal(property.Value.ValueKind, actualJson.RootElement.GetProperty(property.Name).ValueKind);
            Assert.Equal(property.Value.ToString(), actualJson.RootElement.GetProperty(property.Name).ToString());
        }
        Assert.Equal(kind == "document" ? 1 : 0, started);
        Assert.EndsWith("/message/sendMedia/tenant%23instance", f.Transport.Uri!.AbsolutePath);
    }

    [Theory]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.webp", "image/webp")]
    [InlineData("photo.gif", "image/gif")]
    [InlineData("opaque-asset", null)]
    public async Task Image_metadata_matches_extension_without_guessing_an_opaque_url(string name, string? mime)
    {
        using var f = new Fixture("success");
        await f.Gateway.SendImageAsync(f.Workspace, "51999999999", $"https://media.example.test/{name}", "Image", "id", default);
        using var body = JsonDocument.Parse(Assert.Single(f.Transport.Bodies));
        Assert.Equal(name, body.RootElement.GetProperty("fileName").GetString());
        if (mime == null) Assert.False(body.RootElement.TryGetProperty("mimetype", out _));
        else Assert.Equal(mime, body.RootElement.GetProperty("mimetype").GetString());
    }

    [Theory]
    [InlineData("success", MessageStatus.Sent)]
    [InlineData("400", MessageStatus.Failed)]
    [InlineData("timeout", MessageStatus.UnknownDelivery)]
    [InlineData("body-timeout", MessageStatus.UnknownDelivery)]
    [InlineData("network", MessageStatus.UnknownDelivery)]
    [InlineData("missing-id", MessageStatus.UnknownDelivery)]
    [InlineData("invalid-json", MessageStatus.UnknownDelivery)]
    [InlineData("503", MessageStatus.UnknownDelivery)]
    public async Task Real_gateway_and_durable_repository_preserve_outcome_ids_and_disable_automatic_resend(string outcome, MessageStatus expected)
    {
        using var f = new Fixture(outcome);
        var store = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(store.Db);
        SeedConversation(store, f.Workspace);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        notifications.Setup(n => n.NotifyAsync(f.Workspace, "CONVERSATIONS", NotificationType.SystemAlert, It.IsAny<string>(), It.IsAny<string>(), "/inbox", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new OutboundMessageService(repo, f.Gateway, notifications.Object, NullLogger<OutboundMessageService>.Instance);
        async Task<NexFlow.Application.Features.Automation.Conversations.MessageRecord> Send() => await service.SendDocumentAsync(f.Workspace, "chat", "51999999999", "Catálogo", "https://media.example.test/catalog.pdf", "catalog.pdf", "PRODUCT", "inbound-id", default);
        var first = await Send();
        Assert.Equal(expected, first.Status);
        Assert.Equal(first, (await repo.GetOutboundAsync(f.Workspace, first.Id, default))!.Value.Message);
        Assert.False(string.IsNullOrWhiteSpace(first.IdempotencyKey));
        Assert.NotNull(first.TransportStartedAt);
        Assert.Equal(expected == MessageStatus.Sent ? "provider-message" : null, first.ExternalMessageId);
        Assert.Equal(first.Id, (await Send()).Id);
        Assert.True(await service.TryResumeResponseAsync(f.Workspace, "inbound-id", default));
        Assert.Equal(outcome == "400" ? 2 : 1, f.Transport.Bodies.Count);
        if (outcome == "400")
        {
            using var fallback = JsonDocument.Parse(f.Transport.Bodies[1]);
            Assert.Equal(new[] { "delay", "number", "text" }, fallback.RootElement.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal(1200, fallback.RootElement.GetProperty("delay").GetInt32());
            Assert.Contains("por texto", fallback.RootElement.GetProperty("text").GetString());
        }
        Assert.Null(await repo.GetOutboundAsync(Guid.NewGuid(), first.Id, default));
        Assert.Equal(expected.ToString(), store.Read($"workspaces/{f.Workspace}/conversations/chat/messages/{first.Id}")!.Fields["status"].StringValue);
        if (expected != MessageStatus.Sent) notifications.Verify(n => n.NotifyAsync(f.Workspace, "CONVERSATIONS", NotificationType.SystemAlert, It.IsAny<string>(), It.IsAny<string>(), "/inbox", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Concurrent_document_calls_claim_one_transport_and_scope_and_tenant_keys_stay_separate()
    {
        using var f = new Fixture("success");
        var store = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(store.Db);
        SeedConversation(store, f.Workspace);
        var service = new OutboundMessageService(repo, f.Gateway, Mock.Of<INotificationService>(), NullLogger<OutboundMessageService>.Instance);
        Task<NexFlow.Application.Features.Automation.Conversations.MessageRecord> Send(Guid workspace, string scope) => service.SendDocumentAsync(workspace, "chat", "51999999999", "Catálogo", "https://media.example.test/catalog.pdf", "catalog.pdf", scope, "inbound", default);
        var results = await Task.WhenAll(Send(f.Workspace, "PRODUCT"), Send(f.Workspace, "PRODUCT"));
        Assert.Equal(results[0].Id, results[1].Id); Assert.Single(f.Transport.Bodies);
        Assert.Equal(MessageStatus.Sent, (await repo.GetOutboundAsync(f.Workspace, results[0].Id, default))!.Value.Message.Status);
        var other = Guid.NewGuid(); SeedConversation(store, other);
        f.Resolver.Setup(r => r.GetInstanceNameAsync(other, It.IsAny<CancellationToken>())).ReturnsAsync("other-instance");
        var second = await Send(other, "PRODUCT");
        var serviceScope = await Send(f.Workspace, "SERVICE");
        Assert.NotEqual(results[0].Id, second.Id); Assert.NotEqual(results[0].Id, serviceScope.Id);
        Assert.Equal(MessageStatus.Sent, second.Status); Assert.Equal(MessageStatus.Sent, serviceScope.Status);
        Assert.Equal(3, f.Transport.Bodies.Count);
    }

    [Fact]
    public async Task Provider_confirmation_can_repair_unknown_delivery_without_a_second_transport()
    {
        using var f = new Fixture("network");
        var store = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(store.Db);
        SeedConversation(store, f.Workspace);
        var service = new OutboundMessageService(repo, f.Gateway, Mock.Of<INotificationService>(), NullLogger<OutboundMessageService>.Instance);
        var uncertain = await service.SendDocumentAsync(f.Workspace, "chat", "51999999999", "Catálogo", "https://media.example.test/catalog.pdf", "catalog.pdf", "PRODUCT", "inbound", default);
        Assert.Equal(MessageStatus.UnknownDelivery, uncertain.Status);
        await repo.ConfirmOutboundAsync(f.Workspace, uncertain.Id, "late-provider-id", default);
        Assert.True(await service.TryResumeResponseAsync(f.Workspace, "inbound", default));
        var repaired = (await repo.GetOutboundAsync(f.Workspace, uncertain.Id, default))!.Value.Message;
        Assert.Equal(MessageStatus.Sent, repaired.Status); Assert.Equal("late-provider-id", repaired.ExternalMessageId);
        Assert.Equal(uncertain.IdempotencyKey, repaired.IdempotencyKey); Assert.Single(f.Transport.Bodies);
    }

    private static void SeedConversation(InMemoryFirestore store, Guid workspace) => store.Seed($"workspaces/{workspace}/conversations/chat", new Dictionary<string, Value> { ["consumerPhone"] = new() { StringValue = "51999999999" } });

    [Fact]
    public async Task Failed_document_after_a_crash_recovers_one_textual_alternative_without_resending_media()
    {
        using var f = new Fixture("400"); var store = new InMemoryFirestore(); var repo = new FirestoreConversationRepository(store.Db);
        SeedConversation(store, f.Workspace);
        var service = new OutboundMessageService(repo, f.Gateway, Mock.Of<INotificationService>(), NullLogger<OutboundMessageService>.Instance);
        store.BeforeCommit = c => c.Writes.Any(w => w.Update?.Fields.TryGetValue("kind", out var kind) == true && kind.StringValue == "Text") ? new IOException("Crash before text fallback preparation") : null;
        await Assert.ThrowsAsync<IOException>(() => service.SendDocumentAsync(f.Workspace, "chat", "51999999999", "Catálogo", "https://media.example.test/catalog.pdf", "catalog.pdf", "PRODUCT", "inbound", default));
        Assert.Single(f.Transport.Bodies); store.BeforeCommit = null;
        Assert.True(await service.TryResumeResponseAsync(f.Workspace, "inbound", default));
        Assert.True(await service.TryResumeResponseAsync(f.Workspace, "inbound", default));
        Assert.Equal(2, f.Transport.Bodies.Count);
        Assert.Single(f.Transport.Bodies, b => { using var parsed = JsonDocument.Parse(b); return parsed.RootElement.TryGetProperty("mediatype", out _); });
    }

    private sealed class Fixture : IDisposable
    {
        public Guid Workspace { get; } = Guid.NewGuid();
        public Mock<IInstanceResolver> Resolver { get; } = new(MockBehavior.Strict);
        public Transport Transport { get; }
        public HttpClient Http { get; }
        public EvolutionMessageGateway Gateway { get; }
        public Fixture(string outcome)
        {
            Transport = new(outcome); Http = new(Transport);
            Resolver.Setup(r => r.GetInstanceNameAsync(Workspace, It.IsAny<CancellationToken>())).ReturnsAsync("tenant#instance");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Evolution:BaseUrl"] = "https://evolution.example.test", ["Evolution:ApiKey"] = "test-only", ["Evolution:TimeoutSeconds"] = "1" }).Build();
            Gateway = new(Http, configuration, NullLogger<EvolutionMessageGateway>.Instance, Resolver.Object);
        }
        public void Dispose() => Http.Dispose();
    }
    private sealed class Transport(string outcome) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        public Uri? Uri { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct); lock (Bodies) Bodies.Add(body); Uri = request.RequestUri;
            Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
            if (outcome == "timeout") await Task.Delay(Timeout.Infinite, ct);
            if (outcome == "body-timeout") return new(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) };
            if (outcome == "network") throw new HttpRequestException("Simulated lost acknowledgement");
            var providerId = Bodies.Count == 1 ? "provider-message" : $"provider-message-{Bodies.Count}";
            return new(outcome == "400" && request.RequestUri!.AbsolutePath.Contains("sendMedia") ? HttpStatusCode.BadRequest : outcome == "503" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(outcome == "invalid-json" ? "not-json" : outcome == "missing-id" ? "{}" : JsonSerializer.Serialize(new { key = new { id = providerId } }), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return 0; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
