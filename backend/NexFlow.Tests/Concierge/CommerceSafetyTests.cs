using Google.Cloud.Firestore.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class CommerceSafetyTests
{
    private sealed class Durable
    {
        public Guid Workspace { get; } = Guid.NewGuid();
        public InMemoryFirestore Db { get; } = new();
        public FirestoreCatalogRepository Catalog { get; }
        public FirestoreOrderRepository Orders { get; }
        public IContextRecoveryService Context { get; }
        public Mock<INotificationService> Notifications { get; } = new(MockBehavior.Strict);
        public Mock<IEntitlementService> Access { get; } = new(MockBehavior.Strict);
        public OrderFlow Flow { get; }
        public bool Licensed { get; set; } = true;
        public Durable()
        {
            Catalog=new(Db.Db,Db.Resources);var offerings=new OfferingService(Catalog);Orders=new(Db.Db,offerings);
            Context=new ContextRecoveryService(Mock.Of<IConversationCache>(),new FirestoreConversationStateRepository(Db.Db));
            Access.Setup(a=>a.GetAvailableModuleCodesAsync(Workspace,It.IsAny<CancellationToken>())).ReturnsAsync(()=>Licensed ? new[]{"CATALOG","ORDERS"} : []);
            Notifications.Setup(n=>n.NotifyOnceAsync(Workspace,"ORDERS",NotificationType.NewCommercialRequest,It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),"/orders",It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Flow=new(Orders,Notifications.Object,Context,offerings,Access.Object);
        }
        public async Task Seed() => await Catalog.SaveItemAsync(Workspace,new ProductDto { Id="bread",Name="Pan integral",PriceMinorUnits=250 },true,default);
        public Task<string> Turn(string text,string source) => Flow.ProcessAsync(Workspace,"phone","chat","Consumer",text,new(){Intent=ConversationIntent.Order},source,default);
    }

    [Fact]
    public async Task Commit_before_notification_failure_recovers_once_and_blocks_other_writers()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");var attempts=0;
        f.Notifications.Setup(n=>n.NotifyOnceAsync(f.Workspace,"ORDERS",NotificationType.NewCommercialRequest,It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),"/orders",It.IsAny<CancellationToken>()))
            .Returns(()=>++attempts == 1 ? Task.FromException(new IOException("Simulated lost notification")) : Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(()=>f.Turn("envía el pedido","commit"));
        Assert.StartsWith("ORDER_COMMIT:",(await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).CurrentStep);
        Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        await Assert.ThrowsAsync<ConcurrencyException>(()=>f.Turn("Mejor cuatro panes","another"));
        var answer=await f.Turn("envía el pedido","commit");Assert.Equal(answer,await f.Turn("envía el pedido","commit"));
        var saved=Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.Equal(500,saved.TotalAmountMinorUnits);Assert.Equal(OrderStatus.PendingReview,saved.Status);Assert.Equal(2,attempts);
        Assert.Empty((await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).OrderDraftItems);
    }

    [Fact]
    public async Task A_committed_order_recovers_after_license_revocation_without_a_second_purchase()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");var attempts=0;
        f.Notifications.Setup(n=>n.NotifyOnceAsync(f.Workspace,"ORDERS",NotificationType.NewCommercialRequest,It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),"/orders",It.IsAny<CancellationToken>()))
            .Returns(()=>++attempts==1 ? Task.FromException(new IOException("Simulated outage")) : Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(()=>f.Turn("envía el pedido","commit"));f.Licensed=false;
        await f.Turn("envía el pedido","commit");Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.Null((await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).CurrentGoal);Assert.Equal(2,attempts);
    }

    [Fact]
    public async Task Concurrent_commits_have_one_cas_winner_and_one_business_record()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");
        f.Db.SynchronizeReads($"workspaces/{f.Workspace}/conversationStates/phone");
        async Task<bool> Attempt(string source) {try {await f.Turn("envía el pedido",source);return true;}catch(ConcurrencyException){return false;}}
        var results=await Task.WhenAll(Attempt("first"),Attempt("second")).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(results,r=>r);Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.Empty((await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).OrderDraftItems);
    }

    [Theory]
    [InlineData("workspace")] [InlineData("source")] [InlineData("version")]
    public async Task Forged_or_stale_proposals_never_mutate_the_durable_draft(string mismatch)
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");
        var context=await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default);
        var proposal=new OrderDraftProposal(mismatch=="workspace" ? Guid.NewGuid() : f.Workspace,mismatch=="source" ? "foreign" : "edit",
            mismatch=="version" ? "stale" : context.StateVersion,OrderTurnKind.Edit,Array.AsReadOnly(new[]{new OrderEdit(OrderEditKind.Set,"bread",4)}));
        await Assert.ThrowsAsync<ConcurrencyException>(()=>f.Flow.ProcessPlannedAsync(f.Workspace,"phone","chat","Consumer","Mejor cuatro panes",
            new(){Intent=ConversationIntent.Order,OrderProposal=proposal},"edit",DialogueAct.Correction,default));
        Assert.Equal(2,Assert.Single((await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).OrderDraftItems).Quantity);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
    }

    [Fact]
    public async Task Location_scope_is_revalidated_inside_the_existing_order_transaction()
    {
        var f=new Durable();await f.Seed();f.Db.Seed($"workspaces/{f.Workspace}/locations/requested",new Dictionary<string,Value>());var offerings=new Mock<IOfferingService>(MockBehavior.Strict);
        offerings.Setup(o=>o.GetProductByIdAsync(f.Workspace,"bread",It.IsAny<CancellationToken>())).ReturnsAsync(()=>{
            var path=$"workspaces/{f.Workspace}/catalogItems/bread";var fields=f.Db.Read(path)!.Fields.ToDictionary(k=>k.Key,k=>k.Value);
            fields["LocationScope"]=new Value{StringValue="SPECIFIC"};fields["LocationIds"]=new Value{ArrayValue=new ArrayValue()};
            fields["LocationIds"].ArrayValue.Values.Add(new Value{StringValue="other-location"});f.Db.Seed(path,fields);
            return new ProductDto{Id="bread",Name="Pan integral",PriceMinorUnits=250};
        });
        var repo=new FirestoreOrderRepository(f.Db.Db,offerings.Object);
        var order=new OrderRecord{Id="scope-race",ConsumerPhone="phone",ConversationId="chat",RequestedLocationId="requested",Items=[new(){ProductId="bread",Quantity=2}]};
        await Assert.ThrowsAsync<ConcurrencyException>(()=>repo.CreateOrderAsync(f.Workspace,order,default));
        Assert.Null(await repo.GetOrderByIdAsync(f.Workspace,order.Id,default));
    }

    [Fact]
    public async Task A_price_change_after_the_quote_is_rejected_and_never_silently_repriced()
    {
        var f=new Durable();await f.Seed();
        await f.Catalog.SaveItemAsync(f.Workspace,new ProductDto{Id="bread",Name="Pan integral",PriceMinorUnits=999},false,default);
        var request=new OrderRecord{Id="price-race",ConsumerPhone="phone",ConversationId="chat",Items=[new(){ProductId="bread",Quantity=2}],
            ExpectedCatalog=new Dictionary<string,OrderCatalogExpectation>{["bread"]=new("Pan integral",250,"PEN")}};
        await Assert.ThrowsAsync<ConcurrencyException>(()=>f.Orders.CreateOrderAsync(f.Workspace,request,default));
        Assert.Null(await f.Orders.GetOrderByIdAsync(f.Workspace,request.Id,default));
    }

    [Fact]
    public async Task A_checkpoint_without_business_commit_keeps_price_consent_after_restart()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");
        var context=await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default);
        context.CurrentStep=context.CurrentStep!.Replace("ORDER_REVIEW:","ORDER_COMMIT:",StringComparison.Ordinal);
        context.OrderLastSourceMessageId="commit";context.OrderLastResponse=null;await f.Context.SaveContextAsync(f.Workspace,"phone",context,default);
        await f.Catalog.SaveItemAsync(f.Workspace,new ProductDto{Id="bread",Name="Pan integral",PriceMinorUnits=300},false,default);
        await f.Turn("envía el pedido","commit");Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.StartsWith("ORDER_CONFIRM:",(await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).CurrentStep);
    }

    [Fact]
    public async Task A_deleted_location_cannot_be_used_even_when_the_product_scope_is_all()
    {
        var f=new Durable();await f.Seed();
        var request=new OrderRecord{Id="deleted-location",ConsumerPhone="phone",ConversationId="chat",RequestedLocationId="deleted",Items=[new(){ProductId="bread",Quantity=2}]};
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Orders.CreateOrderAsync(f.Workspace,request,default));
        Assert.Null(await f.Orders.GetOrderByIdAsync(f.Workspace,request.Id,default));
    }

    [Fact]
    public async Task Revocation_after_catalog_read_stops_the_commit()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");var checks=0;
        f.Access.Setup(a=>a.GetAvailableModuleCodesAsync(f.Workspace,It.IsAny<CancellationToken>())).ReturnsAsync(()=>++checks==1 ? new[]{"CATALOG","ORDERS"} : []);
        await f.Turn("envía el pedido","commit");Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.Equal(2,Assert.Single((await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).OrderDraftItems).Quantity);
    }

    [Theory]
    [InlineData("ORDER_REVIEW:private-fingerprint")] [InlineData("ORDER_CONFIRM:private-fingerprint")]
    public async Task Internal_price_consent_fingerprints_are_not_sent_to_a_provider(string step)
    {
        var router=new Mock<IAiRouter>();router.Setup(r=>r.ExecuteTaskAsync(AiTaskType.IntentExtraction,It.IsAny<string>(),It.IsAny<string>(),true,It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"Intent\":\"General\",\"RequiresClarification\":true}");
        var clock=new Mock<IClock>();clock.SetupGet(c=>c.UtcNow).Returns(new DateTime(2026,10,20,21,0,0,DateTimeKind.Utc));
        var interpreter=new AiInterpreter(router.Object,clock.Object,Mock.Of<IBusinessProfileRepository>(),NullLogger<AiInterpreter>.Instance);
        await interpreter.InterpretAsync(Guid.NewGuid(),"algo ambiguo","ORDER",step,["ORDERS","CATALOG"],default);
        Assert.DoesNotContain("private-fingerprint",(string)Assert.Single(router.Invocations).Arguments[1]);
    }

    [Theory]
    [InlineData("{\"Intent\":\"Order\",\"OrderProposal\":{\"Quantity\":99}}")]
    [InlineData("{\"Intent\":\"Order\",\"OfferingQuery\":{\"Price\":0}}")]
    [InlineData("{\"Intent\":\"Order\",\"PriceMinorUnits\":1}")]
    [InlineData("{\"Intent\":\"Order\",\"Confirmed\":true}")]
    [InlineData("{\"Intent\":\"Order\",\"WorkspaceId\":\"foreign\"}")]
    public async Task Provider_cannot_submit_server_owned_facts_or_operations(string json)
    {
        var workspace=Guid.NewGuid();var router=new Mock<IAiRouter>();
        router.Setup(r=>r.ExecuteTaskAsync(AiTaskType.IntentExtraction,It.IsAny<string>(),It.IsAny<string>(),true,It.IsAny<CancellationToken>())).ReturnsAsync(json);
        var clock=new Mock<IClock>();clock.SetupGet(c=>c.UtcNow).Returns(new DateTime(2026,10,20,21,0,0,DateTimeKind.Utc));
        var interpreter=new AiInterpreter(router.Object,clock.Object,Mock.Of<IBusinessProfileRepository>(),NullLogger<AiInterpreter>.Instance);
        var result=await interpreter.InterpretAsync(workspace,"Quiero dos panes","",null,["ORDERS","CATALOG"],default);
        Assert.Equal(ConversationIntent.ProviderUnavailable,result.Intent);Assert.Null(result.OrderProposal);Assert.Null(result.OfferingQuery);
    }
}
