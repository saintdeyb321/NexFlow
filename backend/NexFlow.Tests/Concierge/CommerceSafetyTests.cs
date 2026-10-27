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
    [Theory]
    [InlineData("Espera, no limpieza dental: extracción dental", """{"Intent":"Reservation","Act":"Correction","Directive":"ChangeService","Service":"Extracción dental","Evidence":[{"Slot":"Act","Text":"Espera, no limpieza dental: extracción dental","NormalizedValue":"Correction"},{"Slot":"Service","Text":"extracción dental","NormalizedValue":"Extracción dental"}]}""", true)]
    [InlineData("Terminé de elegir", """{"Intent":"Order","Act":"Resume","OrderAction":"Submit","Evidence":[{"Slot":"Act","Text":"Terminé de elegir","NormalizedValue":"Resume"}]}""", true)]
    [InlineData("Suma dos panes", """{"Intent":"Order","Act":"RequestedData","OrderAction":"Edit","Items":[{"Product":"Pan integral","Text":"dos panes","Kind":"Add","Quantity":2,"QuantityText":"dos"}],"Evidence":[{"Slot":"Act","Text":"Suma dos panes","NormalizedValue":"RequestedData"}]}""", true)]
    [InlineData("Pon dos panes", """{"Intent":"Order","Act":"RequestedData","OrderAction":"Edit","Items":[{"Product":"Pan","Text":"panes","Kind":"Add","Quantity":99,"QuantityText":"noventa y nueve"}],"Evidence":[{"Slot":"Act","Text":"Pon dos panes","NormalizedValue":"RequestedData"}]}""", false)]
    [InlineData("Terminé de elegir", """{"Intent":"Order","Act":"Resume","OrderAction":"Submit","Confirmed":true,"Evidence":[{"Slot":"Act","Text":"Terminé de elegir","NormalizedValue":"Resume"}]}""", false)]
    public async Task Conversation_fix_real_interpreter_checks_structured_semantics_without_lexical_triggers(string text, string json, bool valid)
    {
        var router = new Mock<IAiRouter>(); router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction,
            It.IsAny<string>(), text, true, It.IsAny<CancellationToken>())).ReturnsAsync(json);
        var clock = Mock.Of<IClock>(c => c.UtcNow == new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc));
        var interpreter = new AiInterpreter(router.Object, clock, Mock.Of<IBusinessProfileRepository>(), NullLogger<AiInterpreter>.Instance);
        var result = await interpreter.InterpretAsync(Guid.NewGuid(), text, "ORDER", "ORDER_REVIEW:server-hash",
            ["ORDERS", "CATALOG", "SERVICES", "RESERVATIONS"], default);
        Assert.Equal(valid, result.Intent != ConversationIntent.ProviderUnavailable);
        Assert.False(result.Confirmed); Assert.Null(result.OrderProposal); Assert.Null(result.BookingProposal);
    }
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
            Flow=new(Orders,Notifications.Object,Context,offerings,Access.Object,locations:new FirestoreLocationRepository(Db.Db,Db.Resources));
        }
        public async Task Seed() => await Catalog.SaveItemAsync(Workspace,new ProductDto { Id="bread",Name="Pan integral",PriceMinorUnits=250 },true,default);
        public Task<string> Turn(string text,string source) => Flow.ProcessAsync(Workspace,"phone","chat","Consumer",text,new(){Intent=ConversationIntent.Order},source,default);
        public Task<string> Review() => Turn("finalizar", "consent-review");
    }

    [Fact]
    public async Task Commit_before_notification_failure_recovers_once_and_blocks_other_writers()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");await f.Review();var attempts=0;
        f.Notifications.Setup(n=>n.NotifyOnceAsync(f.Workspace,"ORDERS",NotificationType.NewCommercialRequest,It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),"/orders",It.IsAny<CancellationToken>()))
            .Returns(()=>++attempts == 1 ? Task.FromException(new IOException("Simulated lost notification")) : Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(()=>f.Turn("Confirmar","commit"));
        Assert.StartsWith("ORDER_COMMIT:",(await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).CurrentStep);
        Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        await Assert.ThrowsAsync<ConcurrencyException>(()=>f.Turn("Mejor cuatro panes","another"));
        var answer=await f.Turn("Confirmar","commit");Assert.Equal(answer,await f.Turn("Confirmar","commit"));
        var saved=Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.Equal(500,saved.TotalAmountMinorUnits);Assert.Equal(OrderStatus.PendingReview,saved.Status);Assert.Equal(2,attempts);
        Assert.Empty((await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).OrderDraftItems);
    }

    [Fact]
    public async Task A_committed_order_recovers_after_license_revocation_without_a_second_purchase()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");await f.Review();var attempts=0;
        f.Notifications.Setup(n=>n.NotifyOnceAsync(f.Workspace,"ORDERS",NotificationType.NewCommercialRequest,It.IsAny<string>(),It.IsAny<string>(),It.IsAny<string>(),"/orders",It.IsAny<CancellationToken>()))
            .Returns(()=>++attempts==1 ? Task.FromException(new IOException("Simulated outage")) : Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(()=>f.Turn("Confirmar","commit"));f.Licensed=false;
        await f.Turn("Confirmar","commit");Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.Null((await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).CurrentGoal);Assert.Equal(2,attempts);
    }

    [Fact]
    public async Task Concurrent_commits_have_one_cas_winner_and_one_business_record()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");await f.Review();
        f.Db.SynchronizeReads($"workspaces/{f.Workspace}/conversationStates/phone");
        async Task<bool> Attempt(string source) {try {await f.Turn("Confirmar",source);return true;}catch(ConcurrencyException){return false;}}
        var results=await Task.WhenAll(Attempt("first"),Attempt("second")).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(results,r=>r);Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.Empty((await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).OrderDraftItems);
    }

    [Theory]
    [InlineData("workspace")] [InlineData("source")] [InlineData("version")]
    public async Task Forged_or_stale_proposals_never_mutate_the_durable_draft(string mismatch)
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");await f.Review();
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
        var error=await Assert.ThrowsAsync<OrderCatalogChangedException>(()=>repo.CreateOrderAsync(f.Workspace,order,default));
        Assert.Equal(OrderCatalogChange.Location,error.Change);
        Assert.Null(await repo.GetOrderByIdAsync(f.Workspace,order.Id,default));
    }

    [Fact]
    public async Task A_price_change_after_the_quote_is_rejected_and_never_silently_repriced()
    {
        var f=new Durable();await f.Seed();
        await f.Catalog.SaveItemAsync(f.Workspace,new ProductDto{Id="bread",Name="Pan integral",PriceMinorUnits=999},false,default);
        var request=new OrderRecord{Id="price-race",ConsumerPhone="phone",ConversationId="chat",Items=[new(){ProductId="bread",Quantity=2}],
            ExpectedCatalog=new Dictionary<string,OrderCatalogExpectation>{["bread"]=new("Pan integral",250,"PEN")}};
        var error=await Assert.ThrowsAsync<OrderCatalogChangedException>(()=>f.Orders.CreateOrderAsync(f.Workspace,request,default));
        Assert.Equal(OrderCatalogChange.PriceOrCurrency,error.Change);
        Assert.Null(await f.Orders.GetOrderByIdAsync(f.Workspace,request.Id,default));
    }

    [Fact]
    public async Task A_checkpoint_without_business_commit_keeps_price_consent_after_restart()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");await f.Review();
        var context=await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default);
        context.CurrentStep=context.CurrentStep!.Replace("ORDER_CONFIRM:","ORDER_COMMIT:",StringComparison.Ordinal);
        context.OrderLastSourceMessageId="commit";context.OrderLastResponse=null;await f.Context.SaveContextAsync(f.Workspace,"phone",context,default);
        await f.Catalog.SaveItemAsync(f.Workspace,new ProductDto{Id="bread",Name="Pan integral",PriceMinorUnits=300},false,default);
        await f.Turn("Confirmar","commit");Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.StartsWith("ORDER_CONFIRM:",(await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).CurrentStep);
    }

    [Fact]
    public async Task A_deleted_location_cannot_be_used_even_when_the_product_scope_is_all()
    {
        var f=new Durable();await f.Seed();
        var request=new OrderRecord{Id="deleted-location",ConsumerPhone="phone",ConversationId="chat",RequestedLocationId="deleted",Items=[new(){ProductId="bread",Quantity=2}]};
        var error=await Assert.ThrowsAsync<OrderCatalogChangedException>(()=>f.Orders.CreateOrderAsync(f.Workspace,request,default));
        Assert.Equal(OrderCatalogChange.LocationDeleted,error.Change);
        Assert.Null(await f.Orders.GetOrderByIdAsync(f.Workspace,request.Id,default));
    }

    [Fact]
    public async Task Revocation_after_catalog_read_stops_the_commit()
    {
        var f=new Durable();await f.Seed();await f.Turn("Quiero dos panes integrales","draft");await f.Review();var checks=0;
        f.Access.Setup(a=>a.GetAvailableModuleCodesAsync(f.Workspace,It.IsAny<CancellationToken>())).ReturnsAsync(()=>++checks==1 ? new[]{"CATALOG","ORDERS"} : []);
        await f.Turn("Confirmar","commit");Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.Equal(2,Assert.Single((await f.Context.GetOrRecoverContextAsync(f.Workspace,"phone",default)).OrderDraftItems).Quantity);
    }

    [Fact]
    public async Task Reauthorization_after_a_revoked_commit_requires_consent_to_the_updated_quote()
    {
        var f = new Durable();
        await f.Seed();
        var originalQuote = await f.Turn("Quiero dos panes integrales", "draft"); await f.Review();
        Assert.Contains("5.00", originalQuote);
        var checks = 0;
        f.Access.Setup(a => a.GetAvailableModuleCodesAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++checks == 1 ? new[] { "CATALOG", "ORDERS" } : []);

        await f.Turn("Confirmar", "revoked-commit");
        Assert.Equal(2, checks);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace, null, default));
        Assert.Equal(2, Assert.Single((await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default)).OrderDraftItems).Quantity);

        f.Access.Setup(a => a.GetAvailableModuleCodesAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "CATALOG", "ORDERS" });
        await f.Catalog.SaveItemAsync(f.Workspace,
            new ProductDto { Id = "bread", Name = "Pan integral", PriceMinorUnits = 300 }, false, default);

        var response = await f.Turn("Confirmar", "restored-commit");
        var records = (await f.Orders.GetOrdersAsync(f.Workspace, null, default)).ToArray();
        Assert.True(records.Length == 0,
            $"A changed quote requires renewed review before creating an order; actual orders={records.Length}, total minor units={records.FirstOrDefault()?.TotalAmountMinorUnits}.");
        Assert.Contains("6.00", response);
        Assert.StartsWith("ORDER_CONFIRM:", (await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default)).CurrentStep);
        f.Notifications.Verify(n => n.NotifyOnceAsync(f.Workspace, "ORDERS", NotificationType.NewCommercialRequest,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), "/orders", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)] [InlineData("ORDER_COLLECT_ITEMS")]
    [InlineData("ORDER_REVIEW:")] [InlineData("ORDER_REVIEW:invalid")]
    [InlineData("ORDER_CONFIRM:")] [InlineData("ORDER_CONFIRM:obsolete")]
    [InlineData("ORDER_COMMIT:")] [InlineData("ORDER_COMMIT:obsolete")]
    public async Task Missing_or_stale_quote_requires_review_before_a_new_effect(string? step)
    {
        var f = new Durable(); await f.Seed(); await f.Turn("Quiero dos panes integrales", "draft"); await f.Review();
        var state = await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default);
        state.CurrentStep = step; state.OrderLastSourceMessageId = "retry"; state.OrderLastResponse = null;
        await f.Context.SaveContextAsync(f.Workspace, "phone", state, default);
        var answer = await f.Turn("Confirmar", "retry");
        Assert.Equal(answer, await f.Turn("Confirmar", "retry"));
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace, null, default));
        f.Notifications.VerifyNoOtherCalls();
        var reviewed = await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default);
        Assert.StartsWith("ORDER_CONFIRM:", reviewed.CurrentStep); Assert.Contains("¿Confirmas", reviewed.LastQuestion);
        Assert.Equal(2, Assert.Single(reviewed.OrderDraftItems).Quantity); Assert.Contains("S/ 5.00", answer);
        await f.Turn("Confirmar", "fresh-consent");
        await f.Turn("Confirmar", "fresh-consent");
        Assert.Equal(500, Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace, null, default)).TotalAmountMinorUnits);
        Assert.Single(f.Notifications.Invocations);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task Revocation_invalidates_review_even_if_the_price_does_not_change(int revokeAt)
    {
        var f = new Durable(); await f.Seed(); await f.Turn("Quiero dos panes integrales", "draft"); await f.Review();
        var checks = 0;
        f.Access.Setup(a => a.GetAvailableModuleCodesAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++checks == revokeAt ? [] : new[] { "CATALOG", "ORDERS" });
        await f.Turn("Confirmar", "revoked");
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace, null, default)); Assert.Empty(f.Notifications.Invocations);
        var denied = await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default);
        Assert.Null(denied.CurrentStep); Assert.Equal(2, Assert.Single(denied.OrderDraftItems).Quantity);
        f.Access.Setup(a => a.GetAvailableModuleCodesAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "CATALOG", "ORDERS" });
        Assert.Contains("5.00", await f.Turn("Confirmar", "restored"));
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace, null, default)); Assert.Empty(f.Notifications.Invocations);
        Assert.StartsWith("ORDER_CONFIRM:", (await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default)).CurrentStep);
        await f.Turn("Confirmar", "fresh");
        Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace, null, default)); Assert.Single(f.Notifications.Invocations);
    }

    [Theory]
    [InlineData("quantity")] [InlineData("location")] [InlineData("workspace")]
    public async Task Quote_identity_covers_quantities_location_and_workspace(string change)
    {
        var f = new Durable(); await f.Seed(); await f.Turn("Quiero dos panes integrales", "draft"); await f.Review();
        var state = await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default);
        if (change == "quantity") state.OrderDraftItems[0].Quantity = 3;
        if (change == "location") {
            f.Db.Seed($"workspaces/{f.Workspace}/locations/site", new Dictionary<string, Value> { ["Name"] = new() { StringValue = "Synthetic site" } });
            state.SelectedLocationId = "site";
        }
        if (change == "workspace") {
            var other = new Durable(); await other.Seed(); await other.Turn("Quiero dos panes integrales", "other-draft");
            state.CurrentStep = (await other.Context.GetOrRecoverContextAsync(other.Workspace, "phone", default)).CurrentStep;
        }
        await f.Context.SaveContextAsync(f.Workspace, "phone", state, default);
        var answer = await f.Turn("Confirmar", "changed");
        Assert.Contains(change == "quantity" ? "7.50" : "5.00", answer);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace, null, default)); Assert.Empty(f.Notifications.Invocations);
        Assert.StartsWith("ORDER_CONFIRM:", (await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default)).CurrentStep);
        await f.Turn("Confirmar", "fresh");
        var record = Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace, null, default));
        Assert.Equal(change == "quantity" ? 750 : 500, record.TotalAmountMinorUnits);
        Assert.Equal(change == "location" ? "site" : null, record.RequestedLocationId);
    }

    [Fact]
    public async Task A_submit_proposal_cannot_replace_explicit_consent()
    {
        var f = new Durable(); await f.Seed(); await f.Turn("Quiero dos panes integrales", "draft"); await f.Review();
        var state = await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default);
        var proposal = new OrderDraftProposal(f.Workspace, "question", state.StateVersion, OrderTurnKind.Submit, []);
        var plan = await f.Flow.ProcessPlannedAsync(f.Workspace, "phone", "chat", "Consumer", "¿Ya se enviaría?",
            new() { Intent = ConversationIntent.Order, OrderProposal = proposal }, "question", DialogueAct.Confirmation, default);
        Assert.Equal(OperationStatus.AwaitingConfirmation, plan.Operation);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace, null, default)); Assert.Empty(f.Notifications.Invocations);
        await f.Turn("Confirmar", "fresh"); Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace, null, default));
    }

    [Fact]
    public async Task Revocation_during_catalog_recovery_requires_a_new_review_after_restoration()
    {
        var db = new InMemoryFirestore(); var f = new CommerceCertificationFixture(db.Db, db.Resources, Guid.NewGuid());
        await f.Seed(); await f.Turn("Quiero dos panes integrales", "draft"); await f.Review();
        var checks = 0;
        f.Access.Setup(a => a.GetAvailableModuleCodesAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++checks == 3 ? [] : new[] { "CATALOG", "ORDERS" });
        f.AfterReadProduct = async _ => {
            f.AfterReadProduct = null;
            await f.Db.Collection("workspaces").Document(f.Workspace.ToString()).Collection("catalogItems").Document("bread").UpdateAsync("PriceMinorUnits", 300L);
        };
        await f.Turn("Confirmar", "revoked");
        Assert.Equal(3, checks); Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace, null, default));
        Assert.Empty(f.Notifications.Invocations); Assert.Null((await f.State()).CurrentStep);
        Assert.Contains("6.00", await f.Turn("Confirmar", "restored"));
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace, null, default)); Assert.Empty(f.Notifications.Invocations);
        await f.Turn("Confirmar", "fresh");
        Assert.Equal(600, Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace, null, default)).TotalAmountMinorUnits);
    }

    [Fact]
    public async Task Handler_requires_renewed_quote_after_revocation_during_the_commit()
    {
        var f = new AssistantConversationTests.Fixture(intelligentCommerce: true);
        await f.HandleTurn("Quiero dos panes integrales", "draft"); await f.HandleTurn("finalizar", "consent-review");
        f.Firestore.BeforeCommit = request => {
            if (request.Writes.Any(w => w.Update?.Fields.TryGetValue("contextJson", out var json) == true
                && json.StringValue.Contains("ORDER_COMMIT:", StringComparison.Ordinal))) {
                f.Firestore.BeforeCommit = null; f.Modules.Remove("ORDERS");
            }
            return null;
        };
        await f.HandleTurn("Confirmar", "revoked"); Assert.Empty(f.Orders);
        Assert.Equal(2, Assert.Single((await f.Context()).OrderDraftItems).Quantity);
        f.Modules.Add("ORDERS"); f.Products[0].PriceMinorUnits = 300;
        await f.HandleTurn("envía el pedido", "restored"); Assert.Empty(f.Orders);
        Assert.Contains("6.00", f.Sent["restored"].Content);
        Assert.StartsWith("ORDER_CONFIRM:", (await f.Context()).CurrentStep);
        Assert.Equal(ExpectedReply.ConfirmOrder, ConversationQuestion.Current(f.Workspace, await f.Context(), f.Now)?.Expected);
        await f.HandleTurn("sí", "fresh"); await f.HandleTurn("sí", "fresh");
        Assert.Equal(600, Assert.Single(f.Orders.Values).TotalAmountMinorUnits);
    }

    [Fact]
    public async Task A_matching_pending_checkpoint_resumes_only_its_original_source_once()
    {
        var f = new Durable(); await f.Seed(); await f.Turn("Quiero dos panes integrales", "draft"); await f.Review();
        var state = await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default);
        state.CurrentStep = state.CurrentStep!.Replace("ORDER_CONFIRM:", "ORDER_COMMIT:", StringComparison.Ordinal);
        state.OrderLastSourceMessageId = "commit"; state.OrderLastResponse = null;
        await f.Context.SaveContextAsync(f.Workspace, "phone", state, default);
        await Assert.ThrowsAsync<ConcurrencyException>(() => f.Turn("Confirmar", "another"));
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace, null, default)); Assert.Empty(f.Notifications.Invocations);
        await f.Turn("Confirmar", "commit"); await f.Turn("Confirmar", "commit");
        Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace, null, default)); Assert.Single(f.Notifications.Invocations);
        Assert.Empty((await f.Context.GetOrRecoverContextAsync(f.Workspace, "phone", default)).OrderDraftItems);
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
