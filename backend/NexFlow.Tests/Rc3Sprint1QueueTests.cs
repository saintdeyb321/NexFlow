using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Infrastructure.Workers;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3Sprint1QueueTests
{
    [Fact]
    public async Task Diagnostics_identify_the_actual_blocking_head_and_next_inbound_without_crossing_tenants()
    {
        await using var f = await QueueFixture.Create(); var a = Workspace.Create("A"); var b = Workspace.Create("B"); f.Db.Workspaces.AddRange(a, b);
        var headA = Envelope(a, -10, InboundMessageStatus.Failed); headA.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
        var nextA = Envelope(a, -9, InboundMessageStatus.Pending);
        var headB = Envelope(b, -8, InboundMessageStatus.Processing); headB.NextRetryAt = DateTime.UtcNow.AddMinutes(3);
        var nextB = Envelope(b, -7, InboundMessageStatus.Pending);
        f.Db.InboundMessages.AddRange(headA, nextA, headB, nextB); await f.Db.SaveChangesAsync();
        var diagnostics = await f.Repo.GetStalledAsync(DateTime.UtcNow.AddMinutes(-5), 50, default);
        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(nextA.Id, diagnostics.Single(x => x.WorkspaceId == a.Id).BlockedInboundId);
        Assert.Equal(nextB.Id, diagnostics.Single(x => x.WorkspaceId == b.Id).BlockedInboundId);
        Assert.Equal(InboundMessageStatus.Failed, headA.Status); Assert.Equal(InboundMessageStatus.Pending, nextA.Status);
        Assert.Empty(await f.Repo.GetAndLockNextMessagesAsync(10, default));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Fifth_failure_is_durable_dead_letter_and_releases_the_next_turn_even_if_logging_fails(bool throwLogs)
    {
        var logs = new Rc3CaptureLogger<InboundMessageRepository> { Throw = throwLogs };
        await using var f = await QueueFixture.Create(logs); var a = Workspace.Create("A"); f.Db.Workspaces.Add(a);
        var head = Envelope(a, -10, InboundMessageStatus.Failed); head.Attempts = 4; head.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
        var next = Envelope(a, -9, InboundMessageStatus.Pending); f.Db.InboundMessages.AddRange(head, next); await f.Db.SaveChangesAsync();
        var claim = Assert.Single(await f.Repo.GetAndLockNextMessagesAsync(10, default)); Assert.Equal(head.Id, claim.Id);
        await f.Repo.FailMessageAsync(head.Id, claim.ProcessingStartedAt!.Value, "TimeoutException", default);
        Assert.Equal(InboundMessageStatus.DeadLetter, head.Status); Assert.Equal(5, head.Attempts); Assert.Null(head.NextRetryAt);
        Assert.Null(head.ProcessedAt); Assert.Empty(await f.Repo.GetStalledAsync(DateTime.UtcNow, 50, default));
        Assert.Equal(next.Id, Assert.Single(await f.Repo.GetAndLockNextMessagesAsync(10, default)).Id);
        if (!throwLogs) Assert.Contains(logs.Entries, x => x.Contains("DeadLetter") && x.Contains(head.Id.ToString()));
    }

    [Fact]
    public async Task Expired_claim_survives_repository_restart_and_old_completion_cannot_complete_a_new_lease()
    {
        await using var f = await QueueFixture.Create(); var a = Workspace.Create("A"); f.Db.Workspaces.Add(a);
        var head = Envelope(a, -10, InboundMessageStatus.Processing); var oldLease = DateTime.UtcNow.AddMinutes(-6);
        head.ProcessingStartedAt = oldLease; head.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
        var next = Envelope(a, -9, InboundMessageStatus.Pending); f.Db.InboundMessages.AddRange(head, next); await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var restarted = new InboundMessageRepository(f.Db, Mock.Of<IInstanceResolver>(MockBehavior.Strict));
        var claim = Assert.Single(await restarted.GetAndLockNextMessagesAsync(10, default)); Assert.Equal(head.Id, claim.Id);
        await restarted.CompleteMessageAsync(claim.Id, oldLease, default);
        Assert.Equal(InboundMessageStatus.Processing, claim.Status); Assert.Empty(await restarted.GetAndLockNextMessagesAsync(10, default));
        await restarted.CompleteMessageAsync(claim.Id, claim.ProcessingStartedAt!.Value, default);
        Assert.Equal(next.Id, Assert.Single(await restarted.GetAndLockNextMessagesAsync(10, default)).Id);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public async Task Observation_and_alert_failures_cannot_stop_claiming_and_alerts_are_grouped_with_durable_cooldown(bool scanFails, bool alertFails)
    {
        var workspace = Guid.NewGuid(); var repo = new Mock<IInboundMessageRepository>(MockBehavior.Strict);
        var observed = new[] { new InboundQueueDiagnostic { Id = Guid.NewGuid(), WorkspaceId = workspace }, new InboundQueueDiagnostic { Id = Guid.NewGuid(), WorkspaceId = workspace } };
        var scan = repo.Setup(r => r.GetStalledAsync(It.IsAny<DateTime>(), 50, It.IsAny<CancellationToken>()));
        if (scanFails) scan.ThrowsAsync(new IOException("private connection detail")); else scan.ReturnsAsync(observed);
        var claimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.Setup(r => r.GetAndLockNextMessagesAsync(10, It.IsAny<CancellationToken>())).Returns(() => { claimed.TrySetResult(); return Task.FromResult<IEnumerable<InboundMessage>>([]); });
        var notifications = new Mock<INotificationRepository>(MockBehavior.Strict);
        var alert = notifications.Setup(n => n.TryCreateWithCooldownAsync(workspace,
            It.Is<NotificationRecord>(r => r.ModuleCode == "CONVERSATIONS" && r.ActionUrl == "/inbox"), TimeSpan.FromHours(1), It.IsAny<CancellationToken>()));
        if (alertFails) alert.ThrowsAsync(new IOException("private notification detail")); else alert.ReturnsAsync(true);
        using var services = new ServiceCollection().AddSingleton(repo.Object).AddSingleton(notifications.Object).BuildServiceProvider();
        var logger = new Rc3CaptureLogger<InboundMessageWorker>(); using var worker = new InboundMessageWorker(services, logger);
        await worker.StartAsync(default); await claimed.Task.WaitAsync(TimeSpan.FromSeconds(5)); await worker.StopAsync(default);
        notifications.Verify(n => n.TryCreateWithCooldownAsync(workspace, It.IsAny<NotificationRecord>(), TimeSpan.FromHours(1), It.IsAny<CancellationToken>()), scanFails ? Times.Never() : Times.Once());
        Assert.DoesNotContain("private", string.Join("\n", logger.Entries));
    }

    private static InboundMessage Envelope(Workspace w, int minutes, InboundMessageStatus status) => new()
    { Id = Guid.NewGuid(), WorkspaceId = w.Id, InstanceName = w.EvolutionInstanceName!, Phone = "+51999999999", ExternalMessageId = Guid.NewGuid().ToString(), PayloadJson = "{}", Status = status, ReceivedAt = DateTime.UtcNow.AddMinutes(minutes) };

    // Executes the real selection/diagnostic SQL with only the lock suffix removed.
    // It does not certify PostgreSQL row locks, competing processes or SKIP LOCKED.
    private sealed class QueueFixture(SqliteConnection connection, NexFlowDbContext db, InboundMessageRepository repo) : IAsyncDisposable
    {
        public NexFlowDbContext Db => db; public InboundMessageRepository Repo => repo;
        public static async Task<QueueFixture> Create(ILogger<InboundMessageRepository>? logger = null)
        {
            var c = new SqliteConnection("Data Source=:memory:"); await c.OpenAsync();
            c.CreateFunction<string, string, string, string, string>("regexp_replace", (text, pattern, replacement, _) => Regex.Replace(text, pattern, replacement));
            var db = new NexFlowDbContext(new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(c).AddInterceptors(new SelectorDialect()).Options);
            await db.Database.EnsureCreatedAsync(); return new(c, db, new(db, Mock.Of<IInstanceResolver>(MockBehavior.Strict), logger));
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private sealed class SelectorDialect : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { command.CommandText = command.CommandText.Replace("FOR UPDATE OF m SKIP LOCKED", "", StringComparison.Ordinal); return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
    }
}

internal sealed class Rc3CaptureLogger<T> : ILogger<T>
{
    public List<string> Entries { get; } = new(); public bool Throw { get; init; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    { if (Throw) throw new IOException("logging unavailable"); Entries.Add(formatter(state, exception) + (exception?.ToString() ?? "")); }
}
