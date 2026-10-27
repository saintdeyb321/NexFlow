using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Application.Services;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Tests.Concierge;
using Xunit;

namespace NexFlow.Tests.Integration;

// Real Emulator + disposable PostgreSQL; only entitlements and provider transport
// are controlled doubles. Discovered by the existing RC3 integration workflow.
[Trait("Category","Integration")]
[Collection("RC3 isolated stores")]
public sealed class CommerceCertificationIntegrationTests
{
    private static async Task<CommerceCertificationFixture> Create(IsolatedStores stores)
    {
        var workspace=await stores.Workspace(false);
        var f=new CommerceCertificationFixture(stores.Firestore,Mock.Of<NexFlow.Application.Features.Reservations.IReservationResourceCoordinator>(MockBehavior.Strict),workspace);
        // Products do not coordinate reservation epochs. All order/state I/O is real.
        await f.Seed();return f;
    }

    [IsolatedIntegrationFact]
    public async Task Structured_site_survives_restart_legacy_documents_and_workspace_isolation()
    {
        await using var stores=await IsolatedStores.Create();var f=await Create(stores);
        await stores.Root(f.Workspace).Collection("locations").Document("site").SetAsync(new {Name="Synthetic"},cancellationToken:stores.Ct);
        var state=await f.State();state.SelectedLocationId="site";await f.Context.SaveContextAsync(f.Workspace,"synthetic-client",state,stores.Ct);
        await f.Turn("Quiero dos panes integrales","draft");await f.Review();await f.Turn("Confirmar","confirm");
        var restarted=new FirestoreOrderRepository(stores.Firestore,f.Offerings);
        var saved=Assert.Single(await restarted.GetOrdersAsync(f.Workspace,null,stores.Ct));Assert.Equal("site",saved.RequestedLocationId);
        Assert.Null(await restarted.GetOrderByIdAsync(await stores.Workspace(false),saved.Id,stores.Ct));
        var doc=stores.Root(f.Workspace).Collection("orders").Document(saved.Id);
        await doc.UpdateAsync(new Dictionary<string,object>{{"RequestedLocationId",Google.Cloud.Firestore.FieldValue.Delete}},cancellationToken:stores.Ct);
        Assert.Null((await restarted.GetOrderByIdAsync(f.Workspace,saved.Id,stores.Ct))!.RequestedLocationId);
    }

    [IsolatedIntegrationFact] public Task Price_changes_between_snapshot_and_transaction_require_fresh_consent()=>CatalogRace("PriceMinorUnits",300L);
    [IsolatedIntegrationFact] public Task Currency_changes_between_snapshot_and_transaction_require_fresh_consent()=>CatalogRace("Currency","USD");
    [IsolatedIntegrationFact] public Task Location_scope_changes_between_snapshot_and_transaction_preserve_the_cart()=>CatalogRace("LocationScope","SPECIFIC");
    private static async Task CatalogRace(string field,object value)
    {
        await using var stores=await IsolatedStores.Create();var f=await Create(stores);
        await stores.Root(f.Workspace).Collection("locations").Document("site").SetAsync(new {Name="Synthetic"},cancellationToken:stores.Ct);
        var state=await f.State();state.SelectedLocationId="site";await f.Context.SaveContextAsync(f.Workspace,"synthetic-client",state,stores.Ct);
        await f.Turn("Quiero dos panes integrales","draft");await f.Review();
        f.AfterReadProduct=async _=>{
            f.AfterReadProduct=null;
            await stores.Root(f.Workspace).Collection("catalogItems").Document("bread").UpdateAsync(new Dictionary<string,object>{{field,value}},cancellationToken:stores.Ct);
        };
        var answer=await f.Turn("Confirmar","confirm");Assert.Contains("cambió",answer,StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,stores.Ct));Assert.Equal(2,Assert.Single((await f.State()).OrderDraftItems).Quantity);
        if(field!="LocationScope") {
            Assert.StartsWith("ORDER_CONFIRM:",(await f.State()).CurrentStep);
            await f.Turn("Confirmar","fresh");Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,stores.Ct));
        }
    }

    [IsolatedIntegrationFact]
    public async Task Mixed_currencies_and_null_optional_site_roundtrip_without_a_fictitious_total()
    {
        await using var stores=await IsolatedStores.Create();var f=await Create(stores);
        await f.Catalog.SaveItemAsync(f.Workspace,new NexFlow.Application.Features.Catalog.DTOs.ProductDto{Id="usd",Name="Producto USD",Currency="USD",PriceMinorUnits=100},true,stores.Ct);
        var saved=await f.Orders.CreateOrderAsync(f.Workspace,new OrderRecord{Id="mixed",ConsumerPhone="synthetic",ConversationId="chat",Items=[new(){ProductId="bread",Quantity=2},new(){ProductId="usd",Quantity=3}]},stores.Ct);
        var read=(await f.Orders.GetOrderByIdAsync(f.Workspace,saved.Id,stores.Ct))!;
        Assert.Null(read.Currency);Assert.Null(read.TotalAmountMinorUnits);Assert.Null(read.RequestedLocationId);
        Assert.Equal(new[]{"PEN","USD"},read.Items.Select(i=>i.Currency));
    }

    [IsolatedIntegrationFact]
    public async Task Deleted_site_is_not_reused_and_requires_another_explicit_choice()
    {
        await using var stores=await IsolatedStores.Create();var f=await Create(stores);
        var site=stores.Root(f.Workspace).Collection("locations").Document("deleted");await site.SetAsync(new {Name="Synthetic"},cancellationToken:stores.Ct);
        var state=await f.State();state.SelectedLocationId="deleted";await f.Context.SaveContextAsync(f.Workspace,"synthetic-client",state,stores.Ct);
        await f.Turn("Quiero dos panes integrales","draft");await f.Review();f.BeforeCreate=()=>site.DeleteAsync(cancellationToken:stores.Ct);
        var response=await f.Turn("Confirmar","confirm");
        Assert.Contains("sede",response);Assert.Null((await f.State()).SelectedLocationId);Assert.Equal("ORDER_LOCATION",(await f.State()).CurrentStep);
        await f.Turn("Confirmar","second");Assert.Equal("ORDER_LOCATION",(await f.State()).CurrentStep);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,stores.Ct));
    }

    [IsolatedIntegrationFact]
    public async Task Deactivation_during_commit_preserves_the_unsubmitted_draft()
    {
        await using var stores=await IsolatedStores.Create();var f=await Create(stores);await f.Turn("Quiero dos panes integrales","draft");await f.Review();
        f.BeforeCreate=()=>stores.Root(f.Workspace).Collection("catalogItems").Document("bread").UpdateAsync("IsActive",false,cancellationToken:stores.Ct);
        Assert.Contains("disponibilidad",await f.Turn("Confirmar","confirm"));
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,stores.Ct));Assert.Equal(2,Assert.Single((await f.State()).OrderDraftItems).Quantity);
    }

    [IsolatedIntegrationFact]
    public async Task Lost_notification_after_firestore_commit_recovers_after_restart_once_in_postgresql()
    {
        await using var stores=await IsolatedStores.Create();var f=await Create(stores);await using var sql=stores.Db();
        var notifications=new NotificationService(new NotificationRepository(sql));var attempts=0;
        f.Notifications.Setup(n=>n.NotifyOnceAsync(f.Workspace,"ORDERS",NotificationType.NewCommercialRequest,It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),"/orders",It.IsAny<CancellationToken>()))
            .Returns(async(Guid w,string m,NotificationType type,string id,string title,string body,string? url,CancellationToken ct)=>{
                await notifications.NotifyOnceAsync(w,m,type,id,title,body,url,ct);if(++attempts==1) throw new IOException("Lost acknowledgement");
            });
        await f.Turn("Quiero dos panes integrales","draft");await f.Review();await Assert.ThrowsAsync<IOException>(()=>f.Turn("Confirmar","confirm"));
        // Rebuild state/order services, retaining only the durable stores and stable source.
        var restarted=new OrderFlow(new FirestoreOrderRepository(stores.Firestore,f.Offerings),notifications,
            new ContextRecoveryService(Mock.Of<NexFlow.Application.Abstractions.Cache.IConversationCache>(),new FirestoreConversationStateRepository(stores.Firestore)),f.Offerings,f.Access.Object);
        await restarted.ProcessAsync(f.Workspace,"synthetic-client","synthetic-chat","Synthetic","envía el pedido",new(){Intent=ConversationIntent.Order},"confirm",stores.Ct);
        Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,stores.Ct));Assert.Single(await sql.Notifications.AsNoTracking().ToListAsync(stores.Ct));
        Assert.Null((await f.State()).CurrentGoal);
    }

    [IsolatedIntegrationFact]
    public async Task Same_source_and_concurrent_sources_cannot_duplicate_an_order()
    {
        await using var stores=await IsolatedStores.Create();var f=await Create(stores);await f.Turn("Quiero dos panes integrales","draft");await f.Review();
        using var gate=new AsyncGate();f.BeforeCreate=()=>gate.Stop(stores.Ct);
        var first=f.Turn("Confirmar","first");await gate.Reached;
        await Assert.ThrowsAsync<ConcurrencyException>(()=>f.Turn("Confirmar","second"));
        gate.Open();var response=await first;f.BeforeCreate=null;
        Assert.Equal(response,await f.Turn("Confirmar","first"));Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,stores.Ct));
    }

    [IsolatedIntegrationFact]
    public async Task Real_postgresql_module_revocation_and_human_takeover_do_not_create_orders()
    {
        await using var stores=await IsolatedStores.Create();var f=await Create(stores);await using var sql=stores.Db();
        var catalog=NexFlow.Domain.Entities.Module.Create("CATALOG","Synthetic catalog");
        var ordering=NexFlow.Domain.Entities.Module.Create("ORDERS","Synthetic orders");
        sql.Modules.AddRange(catalog,ordering);
        var license=License.CreateCustomLicense(f.Workspace,IsolatedStores.Day.AddDays(-2),IsolatedStores.Day.AddYears(1),2);
        license.AddCustomModule(catalog.Id);license.AddCustomModule(ordering.Id);sql.Licenses.Add(license);await sql.SaveChangesAsync(stores.Ct);
        using var cache=new MemoryCache(new MemoryCacheOptions());
        var access=new EntitlementService(new LicenseRepository(sql),new WorkspaceRepository(sql),new ModuleRepository(sql),
            new MembershipRepository(sql),stores.Clock,cache,Mock.Of<ICurrentUser>(),new SystemAdministratorRepository(sql));
        var flow=new OrderFlow(f.Orders,new NotificationService(new NotificationRepository(sql)),f.Context,f.Offerings,access);
        await flow.ProcessAsync(f.Workspace,"synthetic-client","synthetic-chat","Synthetic","Quiero dos panes integrales",new(){Intent=ConversationIntent.Order},"draft",stores.Ct);
        sql.LicenseModules.Remove(await sql.LicenseModules.SingleAsync(m=>m.LicenseId==license.Id && m.ModuleId==ordering.Id,stores.Ct));
        await sql.SaveChangesAsync(stores.Ct);sql.ChangeTracker.Clear();
        Assert.Equal(new[]{"CATALOG"},await access.GetAvailableModuleCodesAsync(f.Workspace,stores.Ct));
        await flow.ProcessAsync(f.Workspace,"synthetic-client","synthetic-chat","Synthetic","envía el pedido",new(){Intent=ConversationIntent.Order},"confirm",stores.Ct);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,stores.Ct));Assert.Empty(await sql.Notifications.ToListAsync(stores.Ct));
        var state=await f.State();state.Mode="Human";await f.Context.SaveContextAsync(f.Workspace,"synthetic-client",state,stores.Ct);
        await Assert.ThrowsAsync<ConcurrencyException>(()=>flow.ProcessAsync(f.Workspace,"synthetic-client","synthetic-chat","Synthetic","envía el pedido",new(){Intent=ConversationIntent.Order},"new",stores.Ct));
        Assert.Equal(2,Assert.Single((await f.State()).OrderDraftItems).Quantity);
    }

    [IsolatedIntegrationFact]
    public async Task Unknown_delivery_and_failed_delivery_do_not_resend_after_service_restart()
    {
        await using var stores=await IsolatedStores.Create();var f=await Create(stores);
        var repo=new FirestoreConversationRepository(stores.Firestore);var conversation=await repo.GetOrCreateActiveConversationAsync(f.Workspace,"synthetic-client",stores.Ct);
        var gateway=new Mock<IMessageGateway>(MockBehavior.Strict);var attempts=0;
        gateway.Setup(g=>g.SendTextAsync(f.Workspace,"synthetic-client",It.IsAny<string>(),It.IsAny<string>(),It.IsAny<Func<CancellationToken,Task>>(),It.IsAny<CancellationToken>()))
            .Returns(async(Guid _,string _,string _,string _,Func<CancellationToken,Task> starting,CancellationToken ct)=>{
                await starting(ct);attempts++;throw new TimeoutException();
            });
        var outbound=new OutboundMessageService(repo,gateway.Object,f.Notifications.Object,NullLogger<OutboundMessageService>.Instance);
        var response=await outbound.SendMessageAsync(f.Workspace,conversation.Id,"synthetic-client","Synthetic response",SenderType.AI,"original",stores.Ct);
        Assert.Equal(MessageStatus.UnknownDelivery,response.Status);
        var restarted=new OutboundMessageService(new FirestoreConversationRepository(stores.Firestore),gateway.Object,f.Notifications.Object,NullLogger<OutboundMessageService>.Instance);
        Assert.True(await restarted.TryResumeResponseAsync(f.Workspace,"original",stores.Ct));Assert.Equal(1,attempts);
        await repo.RecordOutboundFailureAsync(f.Workspace,response.Id,"Synthetic rejection",MessageStatus.Failed,stores.Ct);
        Assert.True(await restarted.TryResumeResponseAsync(f.Workspace,"original",stores.Ct));Assert.Equal(1,attempts);
    }

    [IsolatedIntegrationFact]
    public async Task Real_postgresql_fifo_keeps_a_pending_turn_behind_failure_until_dead_letter()
    {
        await using var stores=await IsolatedStores.Create();var workspace=await stores.Workspace(false);await using var sql=stores.Db();
        var head=new InboundMessage{WorkspaceId=workspace,InstanceName="synthetic-instance",Phone="synthetic-client",ExternalMessageId="head",PayloadJson="{}",Status=InboundMessageStatus.Failed,Attempts=4,ReceivedAt=DateTime.UtcNow.AddMinutes(-2),NextRetryAt=DateTime.UtcNow.AddMinutes(1)};
        var next=new InboundMessage{WorkspaceId=workspace,InstanceName="synthetic-instance",Phone="synthetic-client",ExternalMessageId="next",PayloadJson="{}",ReceivedAt=DateTime.UtcNow.AddMinutes(-1)};
        sql.InboundMessages.AddRange(head,next);await sql.SaveChangesAsync(stores.Ct);
        var repo=new InboundMessageRepository(sql,Mock.Of<IInstanceResolver>());Assert.Empty(await repo.GetAndLockNextMessagesAsync(10,stores.Ct));
        head.NextRetryAt=DateTime.UtcNow.AddSeconds(-1);await sql.SaveChangesAsync(stores.Ct);
        var claim=Assert.Single(await repo.GetAndLockNextMessagesAsync(10,stores.Ct));Assert.Equal(head.Id,claim.Id);
        await repo.FailMessageAsync(claim.Id,claim.ProcessingStartedAt!.Value,"Synthetic provider timeout",stores.Ct);
        Assert.Equal(InboundMessageStatus.DeadLetter,head.Status);Assert.Equal(next.Id,Assert.Single(await repo.GetAndLockNextMessagesAsync(10,stores.Ct)).Id);
    }
}
