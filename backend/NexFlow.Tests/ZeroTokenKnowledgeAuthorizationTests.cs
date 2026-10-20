using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class ZeroTokenKnowledgeAuthorizationTests
{
    [Theory]
    [InlineData("SERVICES,RESERVATIONS", "donde estan", "ubicaciones")]
    [InlineData("SERVICES,RESERVATIONS", "que horario tienen", "horarios")]
    [InlineData("CATALOG,ORDERS", "donde estan", "ubicaciones")]
    [InlineData("CATALOG,ORDERS", "que horario tienen", "horarios")]
    [InlineData("BUSINESS_HOURS", "donde estan", "ubicaciones")]
    [InlineData("LOCATIONS", "que horario tienen", "horarios")]
    public async Task Handler_denies_unlicensed_shortcuts_without_a_draft_or_restricted_reads_and_persists_one_reply(
        string modules, string text, string subject)
    {
        var f = await Fixture.Create(modules);
        Assert.Null((await f.Contexts.GetOrRecoverContextAsync(f.Workspace, Fixture.Phone, default)).CurrentGoal);
        var command = f.Command(text, "denied");
        await f.Handler.Handle(command, default);
        await f.Handler.Handle(command, default);
        Assert.Contains($"No hay información de {subject}", Assert.Single(f.Sent));
        f.Locations.VerifyNoOtherCalls(); f.Knowledge.VerifyNoOtherCalls(); f.Ai.VerifyNoOtherCalls();
        f.Entitlements.Verify(e => e.GetAvailableModuleCodesAsync(f.Workspace, It.IsAny<CancellationToken>()), Times.Once);
        var messages = (await f.Conversations.GetMessagesAsync(f.Workspace, f.Conversation.Id, 10, default)).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal(MessageStatus.Sent, Assert.Single(messages, m => m.Direction == "outbound").Status);
        Assert.Equal(command.MessageId, Assert.Single(messages, m => m.Direction == "inbound").ExternalMessageId);
    }

    [Theory]
    [InlineData("LOCATIONS", "donde estan", KnowledgeTopic.Locations)]
    [InlineData("BUSINESS_HOURS", "que horario tienen", KnowledgeTopic.BusinessHours)]
    public async Task Handler_authorizes_each_independent_module_using_the_guard_workspace(string module, string text, KnowledgeTopic topic)
    {
        var f = await Fixture.Create(module); f.AllowKnowledge(topic);
        await f.Handler.Handle(f.Command(text, "allowed") with { WorkspaceId = Guid.NewGuid() }, default);
        Assert.Equal("Authorized business facts", Assert.Single(f.Sent));
        f.Entitlements.Verify(e => e.GetAvailableModuleCodesAsync(f.Workspace, It.IsAny<CancellationToken>()), Times.Once);
        f.Knowledge.Verify(k => k.QueryAsync(f.Workspace, It.Is<BusinessKnowledgeSnapshot>(s => s.WorkspaceId == f.Workspace),
            It.Is<KnowledgeQuery>(q => q.Topic == topic), It.IsAny<CancellationToken>()), Times.Once);
        f.Ai.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("LOCATIONS", "donde estan", KnowledgeTopic.Locations, "ubicaciones")]
    [InlineData("BUSINESS_HOURS", "que horario tienen", KnowledgeTopic.BusinessHours, "horarios")]
    public async Task Handler_rechecks_modules_after_revocation_without_reading_restricted_information(
        string module, string text, KnowledgeTopic topic, string subject)
    {
        var f = await Fixture.Create(module); f.AllowKnowledge(topic);
        await f.Handler.Handle(f.Command(text, "before"), default);
        Assert.Equal("Authorized business facts", Assert.Single(f.Sent));
        f.Modules.Remove(module);
        f.Locations.Invocations.Clear(); f.Knowledge.Invocations.Clear();
        await f.Handler.Handle(f.Command(text, "after"), default);
        Assert.Contains($"No hay información de {subject}", f.Sent[1]);
        f.Locations.VerifyNoOtherCalls(); f.Knowledge.VerifyNoOtherCalls(); f.Ai.VerifyNoOtherCalls();
        f.Entitlements.Verify(e => e.GetAvailableModuleCodesAsync(f.Workspace, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handler_keeps_active_reservations_on_the_existing_flow_without_requiring_independent_information_modules()
    {
        var f = await Fixture.Create("SERVICES,RESERVATIONS");
        var context = new ConversationContextDto { CurrentGoal = "RESERVATION", CurrentStep = "COLLECT_LOCATION" };
        await f.Contexts.SaveContextAsync(f.Workspace, Fixture.Phone, context, default);
        var command = f.Command("donde estan", "draft");
        f.Ai.Setup(a => a.RespondAsync(f.Workspace, Fixture.Phone, command, It.IsAny<ConversationRecord>(), default))
            .ReturnsAsync(new MessageRecord { Status = MessageStatus.Sent });
        await f.Handler.Handle(command, default);
        f.Ai.Verify(a => a.RespondAsync(f.Workspace, Fixture.Phone, command, It.IsAny<ConversationRecord>(), default), Times.Once);
        f.Entitlements.VerifyNoOtherCalls(); f.Locations.VerifyNoOtherCalls(); f.Knowledge.VerifyNoOtherCalls();
        Assert.Equal("RESERVATION", (await f.Contexts.GetOrRecoverContextAsync(f.Workspace, Fixture.Phone, default)).CurrentGoal);
    }

    private sealed class Fixture
    {
        public const string Phone = "phone";
        public Guid Workspace { get; } = Guid.NewGuid();
        public HashSet<string> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Mock<IEntitlementService> Entitlements { get; } = new(MockBehavior.Strict);
        public Mock<ILocationRepository> Locations { get; } = new(MockBehavior.Strict);
        public Mock<IKnowledgeService> Knowledge { get; } = new(MockBehavior.Strict);
        public Mock<IAiResponseOrchestrator> Ai { get; } = new(MockBehavior.Strict);
        public List<string> Sent { get; } = new();
        public FirestoreConversationRepository Conversations { get; }
        public IContextRecoveryService Contexts { get; }
        public ConversationRecord Conversation { get; private set; } = null!;
        public ProcessIncomingMessageCommandHandler Handler { get; }

        private Fixture(string modules)
        {
            Modules.UnionWith(modules.Split(',', StringSplitOptions.RemoveEmptyEntries));
            Entitlements.Setup(e => e.GetAvailableModuleCodesAsync(Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(() => Modules.ToArray());
            var store = new InMemoryFirestore();
            Conversations = new FirestoreConversationRepository(store.Db);
            Contexts = new ContextRecoveryService(Mock.Of<IConversationCache>(), new FirestoreConversationStateRepository(store.Db));
            var consumers = new Mock<IConsumerIdentityRepository>(MockBehavior.Strict);
            consumers.Setup(c => c.UpsertConsumerAsync(Workspace, It.IsAny<ConsumerIdentityRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var notifications = Mock.Of<INotificationService>();
            var state = new ConversationStateService(Conversations, consumers.Object, Contexts, Mock.Of<IHumanHandoffService>(MockBehavior.Strict),
                notifications, NullLogger<ConversationStateService>.Instance);
            var gateway = new Mock<IMessageGateway>(MockBehavior.Strict);
            gateway.Setup(g => g.SendTextAsync(Workspace, Phone, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid _, string _, string text, string _, Func<CancellationToken, Task> starting, CancellationToken ct) =>
                { await starting(ct); Sent.Add(text); return $"synthetic-{Sent.Count}"; });
            var outbound = new OutboundMessageService(Conversations, gateway.Object, notifications, NullLogger<OutboundMessageService>.Instance);
            var guard = new Mock<IIncomingMessageGuard>(MockBehavior.Strict);
            guard.Setup(g => g.CheckMessageAsync(It.IsAny<ProcessIncomingMessageCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((true, Workspace, Phone));
            Handler = new(guard.Object, state, Ai.Object, Knowledge.Object, outbound,
                NullLogger<ProcessIncomingMessageCommandHandler>.Instance, Contexts, Locations.Object, Entitlements.Object);
        }

        public static async Task<Fixture> Create(string modules)
        {
            var f = new Fixture(modules);
            f.Conversation = await f.Conversations.GetOrCreateActiveConversationAsync(f.Workspace, Phone, default);
            return f;
        }

        public ProcessIncomingMessageCommand Command(string text, string source) => new("instance", Phone, "Synthetic", text, source, false, Workspace);

        public void AllowKnowledge(KnowledgeTopic topic)
        {
            Locations.Setup(l => l.GetLocationsAsync(Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<NexFlow.Application.Features.Business.LocationDto>());
            Knowledge.Setup(k => k.QueryAsync(Workspace, It.Is<BusinessKnowledgeSnapshot>(s => s.WorkspaceId == Workspace),
                It.Is<KnowledgeQuery>(q => q.Topic == topic), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new KnowledgeResult { Status = KnowledgeStatus.Found, Source = topic, Facts = "Authorized business facts" });
        }
    }
}
