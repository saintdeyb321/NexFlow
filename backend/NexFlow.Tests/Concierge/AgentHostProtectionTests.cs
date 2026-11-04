using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Moq;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.DependencyInjection;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Configuration;
using NexFlow.Infrastructure.DependencyInjection;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentHostProtectionTests : IDisposable
{
    private readonly DirectoryInfo _temporary = Directory.CreateTempSubdirectory("nexflow-guard-");
    private readonly Guid _workspace = Guid.NewGuid();
    private readonly Guid _deployment = Guid.NewGuid();

    private IConfigurationRoot Configuration(Guid dynamicWorkspace, string role = "Demo")
    {
        var path = Path.Combine(_temporary.FullName, "identity.json");
        if (!File.Exists(path)) File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 1, DeploymentId = _deployment, WorkspaceId = _workspace }));
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["NexFlowHost:Role"] = role, ["AgentProtection:ManifestPath"] = path,
            ["AgentProtection:ManifestSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            ["AgentDemo:WorkspaceId"] = dynamicWorkspace == Guid.Empty ? null : dynamicWorkspace.ToString(),
            ["AgentDemo:Enabled"] = "false" }).Build();
    }
    private static ServiceProvider Host(IConfiguration configuration)
    {
        var services = new ServiceCollection().AddApplication();
        services.AddInfrastructure(configuration); // must validate before registering workers/clients
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void Restart_with_omitted_dynamic_options_keeps_the_attested_tenant_protected()
    {
        using (var first = Host(Configuration(_workspace)))
            Assert.True(first.GetRequiredService<IAgentDemoPolicy>().Protects(_workspace));
        using var restarted = Host(Configuration(Guid.Empty));
        var policy = restarted.GetRequiredService<IAgentDemoPolicy>();
        Assert.True(policy.Protects(_workspace));
        Assert.False(policy.Allows(_workspace, "demo", "+51999999999"));
    }

    [Fact]
    public void Restart_with_a_different_dynamic_workspace_refuses_to_build()
    {
        using (var first = Host(Configuration(_workspace)))
            Assert.True(first.GetRequiredService<IAgentDemoPolicy>().Protects(_workspace));
        Assert.Throws<InvalidOperationException>(() => { using var restarted = Host(Configuration(Guid.NewGuid())); });
    }

    [Theory]
    [InlineData("missing-file")][InlineData("missing-pin")][InlineData("bad-pin")][InlineData("changed-file")]
    [InlineData("invalid-json")][InlineData("empty-identity")][InlineData("unknown-schema")][InlineData("oversize")]
    [InlineData("relative-path")][InlineData("missing-both")][InlineData("unknown-role")][InlineData("legacy-role")][InlineData("invalid-options")]
    public void Invalid_attestation_refuses_registration_before_workers_or_clients(string failure)
    {
        var config = Configuration(_workspace); var path = config["AgentProtection:ManifestPath"]!;
        switch (failure) {
            case "missing-file": File.Delete(path); break;
            case "missing-pin": config["AgentProtection:ManifestSha256"] = null; break;
            case "bad-pin": config["AgentProtection:ManifestSha256"] = "not-a-pin"; break;
            case "changed-file": File.AppendAllText(path, " "); break;
            case "relative-path": config["AgentProtection:ManifestPath"] = "identity.json"; break;
            case "missing-both": config["AgentProtection:ManifestPath"] = config["AgentProtection:ManifestSha256"] = null; break;
            case "unknown-role": config["NexFlowHost:Role"] = "unknown"; break;
            case "legacy-role": config["NexFlowHost:Role"] = null; break;
            case "invalid-options": config["AgentDemo:WorkspaceId"] = "synthetic-invalid-identifier"; break;
            default:
                File.WriteAllText(path, failure switch {
                    "invalid-json" => "not-json-synthetic",
                    "empty-identity" => JsonSerializer.Serialize(new { Version = 1, WorkspaceId = Guid.Empty, DeploymentId = _deployment }),
                    "unknown-schema" => JsonSerializer.Serialize(new { Version = 2, WorkspaceId = _workspace, DeploymentId = _deployment }),
                    _ => new string('x', 4097) });
                config["AgentProtection:ManifestSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); break;
        }
        var services = new ServiceCollection().AddApplication(); var count = services.Count;
        var error = Assert.Throws<InvalidOperationException>(() => services.AddInfrastructure(config));
        Assert.StartsWith("DEMO identity preflight:", error.Message);
        Assert.DoesNotContain(path, error.Message); Assert.DoesNotContain(_workspace.ToString(), error.Message);
        Assert.Equal(count, services.Count); Assert.DoesNotContain(services, s => s.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void Two_hosts_share_the_pinned_identity_and_a_discordant_replica_cannot_start()
    {
        var config = Configuration(_workspace);
        using var first = Host(config); using var second = Host(Configuration(Guid.Empty));
        var identity = first.GetRequiredService<AgentProtectionIdentity>();
        Assert.Equal(identity, second.GetRequiredService<AgentProtectionIdentity>());
        Assert.NotSame(identity, second.GetRequiredService<AgentProtectionIdentity>());
        File.WriteAllText(config["AgentProtection:ManifestPath"]!, JsonSerializer.Serialize(new { Version = 1, WorkspaceId = Guid.NewGuid(), DeploymentId = _deployment }));
        Assert.Throws<InvalidOperationException>(() => AgentHostProtection.Load(config));
        Assert.True(first.GetRequiredService<IAgentDemoPolicy>().Protects(_workspace));
        Assert.True(second.GetRequiredService<IAgentDemoPolicy>().Protects(_workspace));
    }

    [Fact]
    public void Ordinary_legacy_host_remains_available_but_cannot_enable_DEMO_by_reload()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var host = Host(config); var policy = host.GetRequiredService<IAgentDemoPolicy>();
        policy.DemandCommercialWrites(_workspace); Assert.False(policy.Protects(_workspace));
        config["AgentDemo:WorkspaceId"] = _workspace.ToString(); config["AgentDemo:Enabled"] = "true";
        config["AgentDemo:InstanceName"] = "instance"; config["AgentDemo:TestPhones:0"] = "+51999999999";
        config["AgentDemo:OperatorAuthorization"] = "SYNTHETIC_DEMO_NO_COMMERCIAL_WRITES"; config.Reload();
        Assert.False(policy.Allows(_workspace, "instance", "+51999999999"));
    }

    [Fact]
    public void Attested_identity_survives_disabled_missing_and_revoked_dynamic_options()
    {
        var config = Configuration(_workspace);
        config["AgentDemo:Enabled"] = "true"; config["AgentDemo:InstanceName"] = "instance";
        config["AgentDemo:TestPhones:0"] = "+51999999999";
        config["AgentDemo:OperatorAuthorization"] = "SYNTHETIC_DEMO_NO_COMMERCIAL_WRITES";
        using var host = Host(config); var policy = host.GetRequiredService<IAgentDemoPolicy>();
        Assert.True(policy.Allows(_workspace, "instance", "+51999999999"));
        config["AgentDemo:Enabled"] = "false"; config.Reload();
        Assert.False(policy.Allows(_workspace, "instance", "+51999999999")); Assert.True(policy.Protects(_workspace));
        config["AgentDemo:Enabled"] = "true"; config["AgentDemo:OperatorAuthorization"] = null; config.Reload();
        Assert.False(policy.Allows(_workspace, "instance", "+51999999999"));
        config["AgentDemo:WorkspaceId"] = null; config.Reload(); File.Delete(config["AgentProtection:ManifestPath"]!);
        Assert.True(policy.Protects(_workspace)); Assert.Throws<DomainException>(() => policy.DemandCommercialWrites(_workspace));
        Assert.Throws<InvalidOperationException>(() => { using var restarted = Host(config); });
        policy.DemandCommercialWrites(Guid.NewGuid());
    }

    private ServiceProvider RestartedHost(Guid? workspace = null)
    {
        var config = Configuration(Guid.Empty);
        if (workspace.HasValue) {
            File.WriteAllText(config["AgentProtection:ManifestPath"]!, JsonSerializer.Serialize(new { Version = 1, DeploymentId = _deployment, WorkspaceId = workspace.Value }));
            config["AgentProtection:ManifestSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(config["AgentProtection:ManifestPath"]!)));
        }
        using (var first = Host(config)) Assert.True(first.GetRequiredService<IAgentDemoPolicy>().Protects(workspace ?? _workspace));
        return Host(config);
    }

    [Fact]
    public async Task Recreated_host_blocks_all_commercial_mutators_without_touching_other_tenants()
    {
        using var host = RestartedHost(); var policy = host.GetRequiredService<IAgentDemoPolicy>();
        var reservation = new Mock<IReservationEngine>(MockBehavior.Strict); var orders = new Mock<IOrderRepository>(MockBehavior.Strict);
        var engine = new DemoReservationEngine(reservation.Object, policy); var repo = new DemoOrderRepository(orders.Object, policy);
        var now = DateTime.UtcNow; var id = Guid.NewGuid();
        Func<Task>[] writes = [
            () => engine.CreateReservationAsync(_workspace, "site", "service", "synthetic", "Fiction", now, default, "source"),
            () => engine.CancelReservationAsync(_workspace, id, default), () => engine.CancelActiveReservationAsync(_workspace, "synthetic", default),
            () => engine.EditReservationAsync(_workspace, id, now, default), () => engine.CompleteReservationAsync(_workspace, id, default),
            () => engine.ApplyExistingOperationAsync(new(_workspace, id, "synthetic", "source", ReservationOperationKind.CancelExistingReservation, 1, now, now, "hash"), default),
            () => engine.ApplyExistingOperationAsync(new(_workspace, id, "synthetic", "source", ReservationOperationKind.RescheduleExistingReservation, 1, now, now, "hash"), default),
            () => repo.CreateOrderAsync(_workspace, new OrderRecord(), default),
            () => repo.UpdateOrderStatusAsync(_workspace, "order", OrderStatus.PendingReview, OrderStatus.PendingReview, default),
            () => repo.UpdateOrderAmountAsync(_workspace, "order", 100, default) ];
        foreach (var write in writes) await Assert.ThrowsAsync<DomainException>(write);
        reservation.VerifyNoOtherCalls(); orders.VerifyNoOtherCalls();
        var other = Guid.NewGuid();
        reservation.Setup(r => r.CancelReservationAsync(other, id, default)).ReturnsAsync(NexFlow.Application.Common.Result.Success());
        reservation.Setup(r => r.GetAvailabilityAsync(_workspace, "site", "service", now, default)).ReturnsAsync([]);
        Assert.True((await engine.CancelReservationAsync(other, id, default)).IsSuccess);
        Assert.Empty(await engine.GetAvailabilityAsync(_workspace, "site", "service", now, default));
    }

    [Theory]
    [InlineData(EntityState.Added, false)][InlineData(EntityState.Modified, false)][InlineData(EntityState.Deleted, false)]
    [InlineData(EntityState.Added, true)][InlineData(EntityState.Modified, true)][InlineData(EntityState.Deleted, true)]
    public async Task Recreated_host_blocks_direct_EF_writes_before_SQL(EntityState state, bool asynchronous)
    {
        using var host = RestartedHost(); var policy = host.GetRequiredService<IAgentDemoPolicy>();
        await using var db = new NexFlowDbContext(new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite("Data Source=:memory:")
            .AddInterceptors(new DemoReservationWriteInterceptor(policy)).Options);
        var entity = Reservation.Create(_workspace, "site", "service", "synthetic", "Fiction", DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        db.Entry(entity).State = state;
        if (asynchronous) await Assert.ThrowsAsync<DomainException>(() => db.SaveChangesAsync());
        else Assert.Throws<DomainException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Recreated_host_rejects_inbound_before_AI_outbound_or_legacy_effects()
    {
        IAgentDemoPolicy? actual = null; var proxy = new Mock<IAgentDemoPolicy>(MockBehavior.Strict);
        proxy.Setup(p => p.Protects(It.IsAny<Guid>())).Returns((Guid w) => actual!.Protects(w));
        proxy.Setup(p => p.Allows(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>())).Returns((Guid w, string i, string p) => actual!.Allows(w, i, p));
        var fixture = new AgentDialogueFixture(demoChannel: true, demoPolicy: proxy.Object);
        using var host = RestartedHost(fixture.Business.Workspace); actual = host.GetRequiredService<IAgentDemoPolicy>();
        await fixture.Business.HandleTurn("Confirma una reserva y un pedido", "restart-inbound");
        Assert.True(actual.Protects(fixture.Business.Workspace)); Assert.Empty(fixture.Inputs);
        Assert.Equal(0, fixture.Business.AiCalls); Assert.Equal(0, fixture.WireCalls);
        Assert.Empty(fixture.Business.Bookings); Assert.Empty(fixture.Business.Orders);
    }

    public void Dispose() => _temporary.Delete(recursive: true);
}
