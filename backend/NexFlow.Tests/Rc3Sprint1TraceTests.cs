using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Workers;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3Sprint1TraceTests
{
    [Theory]
    [InlineData("accepted", MessageStatus.Sent)]
    [InlineData("rejected", MessageStatus.Failed)]
    [InlineData("uncertain", MessageStatus.UnknownDelivery)]
    [InlineData("human", MessageStatus.Pending)]
    public async Task Worker_handler_and_durable_outbound_share_internal_correlation_without_sensitive_logs(string outcome, MessageStatus expected)
    {
        var workspace = Guid.NewGuid(); var inboundId = Guid.NewGuid(); var store = new InMemoryFirestore();
        AssistantRc1AdversarialTests.SeedConversation(store, workspace); var conversationRepo = new FirestoreConversationRepository(store.Db);
        var command = new ProcessIncomingMessageCommand("test-instance", "phone", "private-name", "private-content", "private-provider-inbound", false, workspace);
        var envelope = new InboundMessage { Id = inboundId, WorkspaceId = workspace, Phone = "phone", InstanceName = command.InstanceName,
            ExternalMessageId = command.MessageId, PayloadJson = JsonSerializer.Serialize(command), ProcessingStartedAt = DateTime.UtcNow };
        var repo = new Mock<IInboundMessageRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetStalledAsync(It.IsAny<DateTime>(), 50, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<InboundQueueDiagnostic>());
        var claimed = false;
        repo.Setup(r => r.GetAndLockNextMessagesAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(() => { if (claimed) return Array.Empty<InboundMessage>(); claimed = true; return new[] { envelope }; });
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Simulated SQL processing boundary, not a claim-lock/concurrency proof.
        repo.Setup(r => r.ProcessClaimedAsync(envelope, It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (InboundMessage _, Func<Task> process, CancellationToken _) => { await process(); completed.TrySetResult(); });
        var guard = new Mock<IIncomingMessageGuard>(MockBehavior.Strict);
        guard.Setup(g => g.CheckMessageAsync(It.IsAny<ProcessIncomingMessageCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((true, workspace, "phone"));
        var state = new Mock<IConversationStateService>(MockBehavior.Strict);
        state.Setup(s => s.ProcessStateAsync(workspace, "phone", It.IsAny<ProcessIncomingMessageCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, new ConversationRecord { Id = "chat", Mode = outcome == "human" ? ConversationMode.Human : ConversationMode.Automatic }, outcome == "human" ? null : "private-response"));
        var transports = 0; var gateway = new Mock<IMessageGateway>(MockBehavior.Strict);
        gateway.Setup(g => g.SendTextAsync(workspace, "phone", "private-response", It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, string _, string _, string _, Func<CancellationToken, Task> starting, CancellationToken ct) =>
            {
                await starting(ct); transports++;
                if (outcome == "rejected") throw new HttpRequestException("private-provider-secret", null, HttpStatusCode.BadRequest);
                if (outcome == "uncertain") throw new TimeoutException("private-provider-secret");
                return "private-provider-confirmation";
            });
        var capture = new CaptureProvider();
        using var services = new ServiceCollection().AddLogging(b => b.AddProvider(capture)).AddSingleton(repo.Object)
            .AddSingleton(p => new OutboundMessageService(conversationRepo, gateway.Object, Mock.Of<INotificationService>(), p.GetRequiredService<ILogger<OutboundMessageService>>()))
            .AddSingleton(p => new ProcessIncomingMessageCommandHandler(guard.Object, state.Object, Mock.Of<IAiResponseOrchestrator>(MockBehavior.Strict),
                Mock.Of<IKnowledgeService>(MockBehavior.Strict), p.GetRequiredService<OutboundMessageService>(), p.GetRequiredService<ILogger<ProcessIncomingMessageCommandHandler>>(),
                Mock.Of<IContextRecoveryService>(MockBehavior.Strict), Mock.Of<ILocationRepository>(MockBehavior.Strict)))
            .BuildServiceProvider();
        using var worker = new InboundMessageWorker(services, services.GetRequiredService<ILogger<InboundMessageWorker>>());
        await worker.StartAsync(default); await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)); await worker.StopAsync(default);
        var entries = capture.Entries.ToArray(); var all = string.Join("\n", entries);
        Assert.DoesNotContain("private-", all); Assert.DoesNotContain("51999999999", all);
        Assert.Contains(entries, e => e.Contains("HandlerCompletedNotDelivery") && e.Contains(inboundId.ToString()));
        if (outcome == "human")
        { Assert.Equal(0, transports); Assert.Contains(entries, e => e.Contains("HumanControl") && e.Contains(inboundId.ToString())); return; }
        Assert.All(entries.Where(e => e.Contains("OutboundStage=")), e => { Assert.Contains(inboundId.ToString(), e); Assert.Contains(workspace.ToString(), e); });
        Assert.Contains(entries, e => e.Contains("ResponsePersisted")); Assert.Contains(entries, e => e.Contains("TransportAttempted"));
        Assert.Contains(entries, e => e.Contains($"Status={expected}"));
        Assert.Equal(outcome == "accepted", entries.Any(e => e.Contains("ProviderAcceptedNotDelivered")));
        Assert.True(await services.GetRequiredService<OutboundMessageService>().TryResumeResponseAsync(workspace, command.MessageId, default));
        Assert.Equal(1, transports);
    }

    [Fact]
    public async Task Logging_failure_does_not_turn_provider_acceptance_into_an_uncertain_send()
    {
        var workspace = Guid.NewGuid(); var db = new InMemoryFirestore(); AssistantRc1AdversarialTests.SeedConversation(db, workspace);
        var gateway = new Mock<IMessageGateway>(MockBehavior.Strict); var calls = 0;
        gateway.Setup(g => g.SendTextAsync(workspace, "phone", "response", It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, string _, string _, string _, Func<CancellationToken, Task> starting, CancellationToken ct) => { await starting(ct); calls++; return "provider-id"; });
        var service = new OutboundMessageService(new FirestoreConversationRepository(db.Db), gateway.Object, Mock.Of<INotificationService>(), new Rc3CaptureLogger<OutboundMessageService> { Throw = true });
        Assert.Equal(MessageStatus.Sent, (await service.SendMessageAsync(workspace, "chat", "phone", "response", SenderType.AI, "source", default)).Status);
        Assert.True(await service.TryResumeResponseAsync(workspace, "source", default)); Assert.Equal(1, calls);
    }

    private sealed class CaptureProvider : ILoggerProvider, ISupportExternalScope
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Entries { get; } = new();
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }
        private sealed class Capture(CaptureProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var scopes = new List<string>();
                owner._scopes.ForEachScope((s, list) => list.Add(s is IEnumerable<KeyValuePair<string, object?>> fields ? string.Join(" ", fields.Select(f => $"{f.Key}={f.Value}")) : s?.ToString() ?? ""), scopes);
                owner.Entries.Enqueue(string.Join(" ", scopes) + " " + formatter(state, exception) + exception?.ToString());
            }
        }
    }
}
