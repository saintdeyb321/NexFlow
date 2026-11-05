using Moq;
using NexFlow.Application.Features.Automation.AgentV2;
using NexFlow.Application.Features.Automation.ProcessMessage;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentV2RoutingTests
{
    [Fact]
    public async Task Selected_AgentV2_owns_the_turn_before_any_legacy_model_menu_or_business_flow()
    {
        var ingress = new Mock<IAgentV2Ingress>(MockBehavior.Strict);
        var f = new AssistantConversationTests.Fixture(agentV2Factory: _ => ingress.Object);
        ingress.Setup(i => i.TryHandleAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<ProcessIncomingMessageCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await f.HandleTurn("Necesito orientación sobre el negocio", "v2-route");
        ingress.Verify(i => i.TryHandleAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<ProcessIncomingMessageCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0, f.AiCalls); Assert.Empty(f.Understood); Assert.Empty(f.Plans); Assert.Empty(f.Sent);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }
}
