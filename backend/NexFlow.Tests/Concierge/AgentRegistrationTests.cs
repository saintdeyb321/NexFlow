using Microsoft.Extensions.DependencyInjection;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.DependencyInjection;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Infrastructure.Persistence.Firestore;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentRegistrationTests
{
    [Fact]
    public void Application_registration_resolves_the_planner_drafts_runtime_and_single_DEMO_channel()
    {
        var f = new AgentDialogueFixture(); var services = new ServiceCollection().AddApplication();
        // Infrastructure seams use the existing SDK fixture, never service discovery or credentials.
        services.AddSingleton(f.Clock); services.AddSingleton(f.Router.Object); services.AddSingleton(f.Business.Contexts);
        services.AddSingleton(f.Conversations.Object); services.AddSingleton(f.Business.EntitlementReads.Object);
        services.AddSingleton<IConversationRecoveryService>(f.Business.Recovery);
        services.AddSingleton<IAgentEvaluationClaims>(new FirestoreAgentEvaluationClaims(f.Business.Firestore.Db, f.Clock));
        services.AddSingleton<IAgentTurnJournal>(f.Journal);
        services.AddSingleton<IBusinessReadTools>(new BusinessReadTools(f.Business.KnowledgeReads.Object, f.Business.EntitlementReads.Object));
        services.AddSingleton(f.Business.OfferingReads.Object); services.AddSingleton(Mock.Of<IWorkspaceSlotResolver>());
        services.AddSingleton(Mock.Of<IOutboundMessageService>()); services.AddSingleton(Mock.Of<IHumanHandoffService>());
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.IsType<AgentOwner>(scope.ServiceProvider.GetRequiredService<IConversationalAgent>());
        Assert.IsType<AgentDialogueRuntime>(scope.ServiceProvider.GetRequiredService<IExperimentalAgentRuntime>());
        Assert.IsType<AgentReadTools>(scope.ServiceProvider.GetRequiredService<IAgentReadTools>());
        Assert.IsType<AgentBookingDrafts>(scope.ServiceProvider.GetRequiredService<IAgentBookingDrafts>());
        Assert.IsType<AgentDemoChannel>(Assert.Single(scope.ServiceProvider.GetServices<IAgentDemoChannel>()));
        Assert.False(scope.ServiceProvider.GetRequiredService<IAgentDemoPolicy>().Allows(f.Business.Workspace, "instance", AssistantConversationTests.Fixture.Phone));
    }
}
