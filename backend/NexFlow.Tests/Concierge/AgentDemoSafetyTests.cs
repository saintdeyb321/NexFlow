using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.DependencyInjection;
using Moq;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentDemoSafetyTests
{
    [Fact]
    public void Registered_policy_observes_a_reloaded_kill_switch_without_reopening_commercial_writes()
    {
        var workspace = Guid.NewGuid();
        AgentDemoOptions current = new() { WorkspaceId = workspace, Enabled = true, InstanceName = "isolated-demo",
            OperatorAuthorization = "SYNTHETIC_DEMO_NO_COMMERCIAL_WRITES", TestPhones = ["+51999999999"] };
        var monitor = new Mock<IOptionsMonitor<AgentDemoOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(() => current);
        var services = new ServiceCollection().AddApplication();
        services.AddSingleton(Options.Create(current)); services.AddSingleton(monitor.Object);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IAgentDemoPolicy>();
        Assert.True(policy.Allows(workspace, "isolated-demo", "+51999999999"));
        // Configuration reload replaces the options instance, rather than mutating it.
        current = new() { WorkspaceId = workspace, Enabled = false };
        Assert.False(policy.Allows(workspace, "isolated-demo", "+51999999999"));
        Assert.True(policy.Protects(workspace)); Assert.Throws<DomainException>(() => policy.DemandCommercialWrites(workspace));
    }

    [Theory]
    [InlineData("create")][InlineData("cancel")][InlineData("active")][InlineData("edit")][InlineData("complete")]
    [InlineData("operation-cancel")][InlineData("operation-reschedule")]
    public async Task Protected_workspace_cannot_reach_any_reservation_mutator_even_with_AI_off(string action)
    {
        var ws = Guid.NewGuid(); var settings = new AgentDemoOptions { WorkspaceId = ws, Enabled = false };
        var inner = new Mock<IReservationEngine>(MockBehavior.Strict); var guard = new DemoReservationEngine(inner.Object, new AgentDemoPolicy(Options.Create(settings)));
        var now = DateTime.UtcNow; var id = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<DomainException>(async () => {
            switch (action) {
                case "create": await guard.CreateReservationAsync(ws, "site", "service", "synthetic", "Fiction", now, default, "source"); break;
                case "cancel": await guard.CancelReservationAsync(ws, id, default); break;
                case "active": await guard.CancelActiveReservationAsync(ws, "synthetic", default); break;
                case "edit": await guard.EditReservationAsync(ws, id, now, default); break;
                case "complete": await guard.CompleteReservationAsync(ws, id, default); break;
                default: await guard.ApplyExistingOperationAsync(new(ws, id, "synthetic", "source", action == "operation-cancel"
                    ? ReservationOperationKind.CancelExistingReservation : ReservationOperationKind.RescheduleExistingReservation, 1, now, now, "hash"), default); break;
            }
        });
        Assert.StartsWith("DEMO:", error.Message); inner.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("create")][InlineData("status")][InlineData("amount")]
    public async Task Order_writes_are_blocked_before_the_persistence_adapter(string action)
    {
        var ws = Guid.NewGuid(); var inner = new Mock<IOrderRepository>(MockBehavior.Strict);
        var guarded = new DemoOrderRepository(inner.Object, new AgentDemoPolicy(Options.Create(new AgentDemoOptions { WorkspaceId = ws })));
        await Assert.ThrowsAsync<DomainException>(async () => {
            if (action == "create") await guarded.CreateOrderAsync(ws, new OrderRecord(), default);
            else if (action == "status") await guarded.UpdateOrderStatusAsync(ws, "order", OrderStatus.PendingReview, OrderStatus.PendingReview, default);
            else await guarded.UpdateOrderAmountAsync(ws, "order", 1, default);
        }); inner.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(EntityState.Added)][InlineData(EntityState.Modified)][InlineData(EntityState.Deleted)]
    public async Task Direct_EF_reservation_writes_cannot_bypass_the_DEMO_barrier(EntityState state)
    {
        var ws = Guid.NewGuid(); var policy = new AgentDemoPolicy(Options.Create(new AgentDemoOptions { WorkspaceId = ws }));
        await using var db = new NexFlowDbContext(new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite("Data Source=:memory:")
            .AddInterceptors(new DemoReservationWriteInterceptor(policy)).Options);
        var reservation = Reservation.Create(ws, "site", "service", "synthetic", "Fiction", DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        db.Entry(reservation).State = state;
        var error = await Assert.ThrowsAsync<DomainException>(() => db.SaveChangesAsync()); Assert.StartsWith("DEMO:", error.Message);
        // No schema, migration, PostgreSQL connection or SQL write was needed.
    }

    [Fact]
    public async Task Other_workspaces_and_read_only_availability_keep_the_existing_engine()
    {
        var ws = Guid.NewGuid(); var other = Guid.NewGuid(); var inner = new Mock<IReservationEngine>(MockBehavior.Strict);
        inner.Setup(e => e.GetAvailabilityAsync(ws, "site", "service", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        inner.Setup(e => e.CancelReservationAsync(other, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(NexFlow.Application.Common.Result.Success());
        var engine = new DemoReservationEngine(inner.Object, new AgentDemoPolicy(Options.Create(new AgentDemoOptions { WorkspaceId = ws })));
        Assert.Empty(await engine.GetAvailabilityAsync(ws, "site", "service", DateTime.UtcNow, default));
        Assert.True((await engine.CancelReservationAsync(other, Guid.NewGuid(), default)).IsSuccess);
        Assert.Equal(2, inner.Invocations.Count);
    }

    [Fact]
    public void Only_server_authorized_instance_and_test_recipient_can_enter_and_kill_switch_preserves_protection()
    {
        var ws = Guid.NewGuid(); var o = new AgentDemoOptions { WorkspaceId = ws, Enabled = true, InstanceName = "isolated-demo",
            OperatorAuthorization = "SYNTHETIC_DEMO_NO_COMMERCIAL_WRITES", TestPhones = ["+51999999999"] };
        var p = new AgentDemoPolicy(Options.Create(o)); Assert.True(p.Allows(ws, "isolated-demo", "+51999999999"));
        Assert.False(p.Allows(Guid.NewGuid(), "isolated-demo", "+51999999999")); Assert.False(p.Allows(ws, "other-instance", "+51999999999"));
        Assert.False(p.Allows(ws, "isolated-demo", "+51911111111")); o.Enabled = false;
        Assert.False(p.Allows(ws, "isolated-demo", "+51999999999")); Assert.True(p.Protects(ws));
        Assert.Throws<DomainException>(() => p.DemandCommercialWrites(ws));
    }
}
