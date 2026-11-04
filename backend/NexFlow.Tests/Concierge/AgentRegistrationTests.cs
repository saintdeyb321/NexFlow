using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.DependencyInjection;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Domain.Exceptions;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentRegistrationTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Production_registration_shares_the_policy_and_installs_the_EF_and_order_barriers(bool applicationLoaded)
    {
        var workspace = Guid.NewGuid();
        AgentDemoOptions current = new() { WorkspaceId = workspace };
        var monitor = new Mock<IOptionsMonitor<AgentDemoOptions>>(); monitor.SetupGet(m => m.CurrentValue).Returns(() => current);
        var services = new ServiceCollection(); if (applicationLoaded) services.AddApplication();
        // No Firebase project: AddInfrastructure must not construct an ADC-backed client.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=synthetic_unused;Username=synthetic" }).Build();
        services.AddInfrastructure(configuration); services.AddSingleton(monitor.Object);
        services.AddSingleton(new NexFlow.Tests.Fakes.InMemoryFirestore().Db);
        services.AddSingleton(Mock.Of<IOfferingService>(MockBehavior.Strict));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope(); var policy = first.ServiceProvider.GetRequiredService<IAgentDemoPolicy>();
        Assert.True(policy.Protects(workspace)); current = new();
        using var second = provider.CreateScope(); Assert.Same(policy, second.ServiceProvider.GetRequiredService<IAgentDemoPolicy>());
        Assert.True(policy.Protects(workspace));
        var options = second.ServiceProvider.GetRequiredService<DbContextOptions<NexFlowDbContext>>();
        Assert.IsType<DemoReservationWriteInterceptor>(Assert.Single(options.FindExtension<CoreOptionsExtension>()!.Interceptors!));
        var orders = Assert.IsType<DemoOrderRepository>(second.ServiceProvider.GetRequiredService<IOrderRepository>());
        await Assert.ThrowsAsync<DomainException>(() => orders.CreateOrderAsync(workspace, new OrderRecord(), default));
        // Merely constructing options does not open PostgreSQL/Redis or run workers.
    }

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
