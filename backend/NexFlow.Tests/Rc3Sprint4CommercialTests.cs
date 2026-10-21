using NexFlow.Application.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Application.Features.Requests;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3Sprint4CommercialTests
{
    [Fact]
    public async Task Order_snapshots_current_authorized_catalog_and_replay_never_reprices_history()
    {
        var f = await Orders.Create(); var request = Orders.Order("one");
        request.Items[0].ProductName = "Invented"; request.Items[0].UnitPriceMinorUnits = 1;
        var original = await f.Repository.CreateOrderAsync(f.Workspace, request, default);
        Assert.Equal("Product", original.Items[0].ProductName); Assert.Equal(500, original.TotalAmountMinorUnits);
        var changed = Orders.Product(); changed.Name = "Changed"; changed.PriceMinorUnits = 999;
        await f.Catalog.SaveItemAsync(f.Workspace, changed, false, default);
        var replay = await f.Repository.CreateOrderAsync(f.Workspace, Orders.Order("one"), default);
        Assert.Equal("Product", replay.Items[0].ProductName); Assert.Equal(250, replay.Items[0].UnitPriceMinorUnits);
        Assert.Equal(500, replay.TotalAmountMinorUnits);
        var next = await f.Repository.CreateOrderAsync(f.Workspace, Orders.Order("two"), default);
        Assert.Equal(1998, next.TotalAmountMinorUnits); Assert.NotEqual(replay.Id, next.Id);
        Assert.Equal(2, (await f.Repository.GetOrdersAsync(f.Workspace, null, default)).Count());
        Assert.Null(await f.Repository.GetOrderByIdAsync(Guid.NewGuid(), "one", default));
        var collision = Orders.Order("one"); collision.Items[0].Quantity = 3;
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Repository.CreateOrderAsync(f.Workspace, collision, default));
    }

    [Fact]
    public async Task Concurrent_order_creation_and_lost_commit_ack_recover_one_identity()
    {
        var f = await Orders.Create(); f.Db.SynchronizeReads($"workspaces/{f.Workspace}/orders/one");
        var results = await Task.WhenAll(f.Repository.CreateOrderAsync(f.Workspace, Orders.Order("one"), default),
            f.Repository.CreateOrderAsync(f.Workspace, Orders.Order("one"), default));
        Assert.Equal(results[0].Id, results[1].Id);
        var fail = true;
        f.Db.AfterCommit = commit => fail && commit.Writes.Any(w => w.Update?.Name.EndsWith("/orders/two") == true)
            ? new IOException("ACK lost after order commit") : null;
        await Assert.ThrowsAsync<IOException>(() => f.Repository.CreateOrderAsync(f.Workspace, Orders.Order("two"), default));
        fail = false;
        var replay = await f.Repository.CreateOrderAsync(f.Workspace, Orders.Order("two"), default);
        Assert.Equal("two", replay.Id); Assert.Equal(2, (await f.Repository.GetOrdersAsync(f.Workspace, null, default)).Count());
    }

    [Theory]
    [InlineData("missing")] [InlineData("inactive")] [InlineData("zero")] [InlineData("negative")] [InlineData("overflow")]
    public async Task Order_rejects_unavailable_products_invalid_quantities_and_overflow(string problem)
    {
        var f = await Orders.Create(); var request = Orders.Order("invalid");
        if (problem == "missing") request.Items[0].ProductId = "absent";
        if (problem == "inactive") await f.Catalog.DeleteItemAsync(f.Workspace, "product", default);
        if (problem == "zero") request.Items[0].Quantity = 0;
        if (problem == "negative") request.Items[0].Quantity = -1;
        if (problem == "overflow")
        { var huge = Orders.Product(); huge.PriceMinorUnits = long.MaxValue; await f.Catalog.SaveItemAsync(f.Workspace, huge, false, default); }
        await Assert.ThrowsAsync<ArgumentException>(() => f.Repository.CreateOrderAsync(f.Workspace, request, default));
        Assert.Null(await f.Repository.GetOrderByIdAsync(f.Workspace, "invalid", default));
    }

    [Fact]
    public async Task Mixed_currencies_remain_pending_review_without_inventing_a_total()
    {
        var f = await Orders.Create(); var usd = Orders.Product(); usd.Id = "usd"; usd.Currency = "USD";
        await f.Catalog.SaveItemAsync(f.Workspace, usd, true, default);
        var request = Orders.Order("mixed"); request.Items.Add(new() { ProductId = "usd", Quantity = 1 });
        await f.Repository.CreateOrderAsync(f.Workspace, request, default);
        var saved = (await f.Repository.GetOrderByIdAsync(f.Workspace, "mixed", default))!;
        Assert.Null(saved.Currency); Assert.Null(saved.TotalAmountMinorUnits);
        Assert.Equal(new[] { "PEN", "USD" }, saved.Items.Select(i => i.Currency));
        Assert.Contains("monedas diferentes", saved.Notes);
    }

    [Fact]
    public async Task Catalog_edit_between_authorized_read_and_order_commit_is_rejected_without_stale_snapshot()
    {
        var f = await Orders.Create(); var offerings = new Mock<IOfferingService>(MockBehavior.Strict);
        offerings.Setup(o => o.GetProductByIdAsync(f.Workspace, "product", default)).Returns(async () =>
        {
            var old = Orders.Product(); var changed = Orders.Product(); changed.PriceMinorUnits = 999;
            await f.Catalog.SaveItemAsync(f.Workspace, changed, false, default); return old;
        });
        var repo = new FirestoreOrderRepository(f.Db.Db, offerings.Object);
        await Assert.ThrowsAsync<ConcurrencyException>(() => repo.CreateOrderAsync(f.Workspace, Orders.Order("race"), default));
        Assert.Null(await repo.GetOrderByIdAsync(f.Workspace, "race", default));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Request_lost_firestore_ack_or_notification_failure_recovers_business_and_notification_once(bool lostBusinessAck)
    {
        var db = new InMemoryFirestore(); var workspace = Guid.NewGuid();
        var repo = new FirestoreRequestRepository(db.Db);
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(connection).Options;
        await using var sql = new NotificationDb(options); await sql.Database.EnsureCreatedAsync();
        var actualNotifications = new NotificationRepository(sql);
        var attempts = 0;
        var boundary = new Mock<INotificationRepository>(MockBehavior.Strict);
        boundary.Setup(n => n.CreateAsync(workspace, It.IsAny<NotificationRecord>(), default)).Returns(async (Guid w, NotificationRecord record, CancellationToken ct) =>
        {
            attempts++;
            if (!lostBusinessAck && attempts == 1) throw new IOException("SQL unavailable after Firestore business commit");
            await actualNotifications.CreateAsync(w, record, ct);
            if (!lostBusinessAck && attempts == 2) throw new IOException("SQL notification committed but ACK lost");
        });
        RequestService Service() => new(new FirestoreRequestRepository(db.Db), new NotificationService(boundary.Object));
        Task<string> Create(RequestService service) => service.CreateRequestAsync(workspace, "phone", "conversation", RequestType.CommercialInquiry,
            "Request", "Description", "source", null, default);
        if (lostBusinessAck) db.AfterCommit = commit => commit.Writes.Any(w => w.Update?.Name.Contains("/requests/") == true)
            ? new IOException("Business committed but ACK lost") : null;
        await Assert.ThrowsAsync<IOException>(() => Create(Service()));
        Assert.Single(await repo.GetRequestsAsync(workspace, 10, null, default));
        Assert.Empty(await actualNotifications.GetUnreadAsync(workspace, 10, default));
        db.AfterCommit = null;
        if (!lostBusinessAck) await Assert.ThrowsAsync<IOException>(() => Create(Service()));
        var recovered = await Create(Service()); var replay = await Create(Service());
        Assert.Equal(recovered, replay);
        Assert.Single(await repo.GetRequestsAsync(workspace, 10, null, default));
        Assert.Single(await actualNotifications.GetUnreadAsync(workspace, 10, default));
        Assert.Empty(await repo.GetRequestsAsync(Guid.NewGuid(), 10, null, default));
    }

    [Fact]
    public async Task Notification_identity_is_per_business_operation_module_and_workspace_and_read_rows_remain_deduplicated()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var sql = new NotificationDb(new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(connection).Options);
        await sql.Database.EnsureCreatedAsync(); var repo = new NotificationRepository(sql); var service = new NotificationService(repo);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        Task Notify(Guid w, string module, string id) => service.NotifyOnceAsync(w, module, NotificationType.NewCommercialRequest, id, "Title", "Message", "/requests", default);
        await Notify(a, "REQUESTS", "one"); var first = Assert.Single(await repo.GetUnreadAsync(a, 10, default));
        await repo.MarkAsReadAsync(a, first.Id, default); await Notify(a, "REQUESTS", "one");
        Assert.Empty(await repo.GetUnreadAsync(a, 10, default));
        await Notify(a, "REQUESTS", "two"); await Notify(a, "ORDERS", "one"); await Notify(b, "REQUESTS", "one");
        Assert.Equal(4, await sql.Notifications.CountAsync());
        Assert.Single(await repo.GetUnreadAsync(b, 10, default));
    }

    [Fact]
    public async Task Order_flow_recovers_notification_after_business_commit_without_losing_draft_or_creating_second_order()
    {
        var f = await Orders.Create();
        var durable = new ConversationContextDto { CurrentGoal = "ORDER", OrderDraftItems = [new() { ProductId = "product", ProductName = "Product", Quantity = 2 }] };
        ConversationContextDto Clone(ConversationContextDto value) => System.Text.Json.JsonSerializer.Deserialize<ConversationContextDto>(System.Text.Json.JsonSerializer.Serialize(value))!;
        var contexts = new Mock<IContextRecoveryService>(MockBehavior.Strict);
        contexts.Setup(c => c.GetOrRecoverContextAsync(f.Workspace, "phone", default)).ReturnsAsync(() => Clone(durable));
        contexts.Setup(c => c.SaveContextAsync(f.Workspace, "phone", It.IsAny<ConversationContextDto>(), default))
            .Callback<Guid, string, ConversationContextDto, CancellationToken>((_, _, value, _) => durable = Clone(value)).Returns(Task.CompletedTask);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict); var attempts = 0;
        notifications.Setup(n => n.NotifyOnceAsync(f.Workspace, "ORDERS", NotificationType.NewCommercialRequest,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), "/orders", default)).Returns(() =>
            ++attempts == 1 ? Task.FromException(new IOException("Notification unavailable")) : Task.CompletedTask);
        var access = new Mock<IEntitlementService>();
        access.Setup(e => e.GetAvailableModuleCodesAsync(f.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "ORDERS", "CATALOG" });
        var flow = new OrderFlow(f.Repository, notifications.Object, contexts.Object, new OfferingService(f.Catalog), access.Object);
        Task<string> Send() => flow.ProcessAsync(f.Workspace, "phone", "conversation", "Customer", "enviar pedido", new AiInterpretation(), "source", default);
        await Assert.ThrowsAsync<IOException>(Send);
        Assert.Single(await f.Repository.GetOrdersAsync(f.Workspace, null, default)); Assert.Single(durable.OrderDraftItems);
        var response = await Send(); Assert.Equal(response, await Send());
        Assert.Equal(2, attempts); Assert.Empty(durable.OrderDraftItems);
        Assert.Single(await f.Repository.GetOrdersAsync(f.Workspace, null, default));
    }

    [Theory]
    [InlineData(true, "PK_Notifications", true)]
    [InlineData(false, "PK_Notifications", false)]
    [InlineData(true, "another_constraint", false)]
    public async Task Notification_primary_key_race_only_recovers_the_same_workspace_and_own_constraint(bool sameWorkspace, string constraint, bool recovered)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(connection).Options;
        await using var sql = new NotificationDb(options); await sql.Database.EnsureCreatedAsync();
        var workspace = Guid.NewGuid(); var record = new NotificationRecord { ModuleCode = "REQUESTS", Title = "Title" };
        sql.BeforeSave = async () =>
        {
            await using var winner = new NotificationDb(options);
            winner.Notifications.Add(Notification.Create(sameWorkspace ? workspace : Guid.NewGuid(), "REQUESTS", "SystemAlert", "Title", "Message", null, Guid.Parse(record.Id)));
            await winner.SaveChangesAsync();
            // Simulated PostgreSQL error after another writer won the key. This
            // exercises recovery classification; SQLite does not prove PG locking.
            throw new DbUpdateException("Concurrent insert", new Npgsql.PostgresException("duplicate", "ERROR", "ERROR", "23505", constraintName: constraint));
        };
        var repo = new NotificationRepository(sql);
        if (recovered) await repo.CreateAsync(workspace, record, default);
        else await Assert.ThrowsAsync<DbUpdateException>(() => repo.CreateAsync(workspace, record, default));
        Assert.Equal(1, await sql.Notifications.CountAsync());
        if (recovered) Assert.DoesNotContain(sql.ChangeTracker.Entries<Notification>(), e => e.State == EntityState.Added);
    }

    private sealed class NotificationDb(DbContextOptions<NexFlowDbContext> options) : NexFlowDbContext(options)
    {
        public Func<Task>? BeforeSave { get; set; }
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (BeforeSave != null) await BeforeSave();
            return await base.SaveChangesAsync(cancellationToken);
        }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            foreach (var entity in builder.Model.GetEntityTypes().ToArray()) builder.Ignore(entity.ClrType);
            builder.Entity<Notification>().HasKey(n => n.Id);
        }
    }

    private sealed class Orders
    {
        public InMemoryFirestore Db { get; } = new(); public Guid Workspace { get; } = Guid.NewGuid();
        public FirestoreCatalogRepository Catalog { get; } public FirestoreOrderRepository Repository { get; }
        private Orders() { Catalog = new(Db.Db, Db.Resources); Repository = new(Db.Db, new OfferingService(Catalog)); }
        public static async Task<Orders> Create()
        { var f = new Orders(); await f.Catalog.SaveItemAsync(f.Workspace, Product(), true, default); return f; }
        public static ProductDto Product() => new() { Id = "product", Name = "Product", Currency = "PEN", PriceMinorUnits = 250, IsActive = true, LocationScope = "ALL" };
        public static OrderRecord Order(string id) => new() { Id = id, ConsumerPhone = "phone", ConversationId = "conversation", ConsumerName = "Customer",
            Items = [new() { ProductId = "product", Quantity = 2 }] };
    }
}
