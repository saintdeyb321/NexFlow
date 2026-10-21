using Google.Cloud.Firestore.V1;
using Moq;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Domain.Exceptions;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class CommerceHardeningTests
{
    [Theory]
    [InlineData("Hola, buenas tardes")] [InlineData("hola buenos días")] [InlineData("Hola! Buenas noches")]
    public async Task Compound_greetings_do_not_consume_an_interpretation_call(string text)
    {
        var f=new AssistantConversationTests.Fixture(conciergeCatalog:true,intelligentCommerce:true);
        await f.HandleTurn(text,"greeting");Assert.Equal(0,f.AiCalls);Assert.Empty(f.Wording.Invocations);
        Assert.NotEmpty(f.Sent["greeting"].Content);Assert.Empty(f.Orders);Assert.Empty(f.Bookings);
    }
    [Fact]
    public async Task Catalog_mutation_after_transaction_reads_aborts_then_recovers_without_a_stale_order()
    {
        var db=new InMemoryFirestore();var f=new CommerceCertificationFixture(db.Db,db.Resources,Guid.NewGuid());await f.Seed();
        await f.Turn("Quiero dos panes integrales","draft");
        db.BeforeCommit=request=>{
            if(!request.Writes.Any(w=>w.Update?.Name.Contains("/orders/",StringComparison.Ordinal)==true)) return null;
            db.BeforeCommit=null;var path=$"workspaces/{f.Workspace}/catalogItems/bread";
            var fields=db.Read(path)!.Fields.ToDictionary(p=>p.Key,p=>p.Value);
            fields["PriceMinorUnits"]=new Value{IntegerValue=350};db.Seed(path,fields);return null;
        };
        var reply=await f.Turn("envía el pedido","confirm");
        Assert.True(db.AbortedCommits>0);Assert.Contains("7.00",reply);
        Assert.StartsWith("ORDER_CONFIRM:",(await f.State()).CurrentStep);Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        await f.Turn("envía el pedido","fresh");Assert.Equal(700,Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default)).TotalAmountMinorUnits);
    }

    [Fact]
    public async Task Human_takeover_during_catalog_recovery_cannot_be_overwritten_by_the_old_turn()
    {
        var db=new InMemoryFirestore();var f=new CommerceCertificationFixture(db.Db,db.Resources,Guid.NewGuid());await f.Seed();await f.Turn("Quiero dos panes integrales","draft");
        f.AfterReadProduct=async _=>{
            f.AfterReadProduct=null;var human=await f.State();human.Mode="Human";await f.Context.SaveContextAsync(f.Workspace,"synthetic-client",human,default);
            await db.Db.Collection("workspaces").Document(f.Workspace.ToString()).Collection("catalogItems").Document("bread").UpdateAsync("PriceMinorUnits",500L);
        };
        await Assert.ThrowsAsync<ConcurrencyException>(()=>f.Turn("envía el pedido","confirm"));
        Assert.Equal("Human",(await f.State()).Mode);Assert.Equal(2,Assert.Single((await f.State()).OrderDraftItems).Quantity);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
    }

    [Theory]
    [InlineData("¿Dónde queda su sede?")] [InlineData("¿Qué horario tienen?")]
    [InlineData("¿A qué hora atienden?")] [InlineData("¿Cuál es su dirección?")]
    public async Task Independent_information_never_reads_the_commercial_catalog_even_with_a_draft(string text)
    {
        var slots=new Mock<IWorkspaceSlotResolver>(MockBehavior.Strict);
        var resolver=new CommerceTurnResolver(slots.Object,new());
        Assert.Null(await resolver.ResolveAsync(Guid.NewGuid(),"info",text,new ConversationContextDto{CurrentGoal="ORDER"},["CATALOG","ORDERS"],default));
        Assert.Empty(slots.Invocations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Generic_concurrency_and_unknown_transport_failures_are_not_catalog_recoveries(bool transport)
    {
        var db=new InMemoryFirestore();var f=new CommerceCertificationFixture(db.Db,db.Resources,Guid.NewGuid());
        await f.Seed();await f.Turn("Quiero dos panes integrales","draft");
        f.BeforeCreate=()=>Task.FromException(transport ? new IOException("Unknown commit result") : new ConcurrencyException("CAS conflict"));
        if(transport) await Assert.ThrowsAsync<IOException>(()=>f.Turn("envía el pedido","commit"));
        else await Assert.ThrowsAsync<ConcurrencyException>(()=>f.Turn("envía el pedido","commit"));
        Assert.StartsWith("ORDER_COMMIT:",(await f.State()).CurrentStep);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
    }

    [Fact]
    public async Task An_already_committed_identity_is_resumed_instead_of_quoted_or_created_again()
    {
        var db=new InMemoryFirestore();var f=new CommerceCertificationFixture(db.Db,db.Resources,Guid.NewGuid());
        await f.Seed();await f.Turn("Quiero dos panes integrales","draft");
        f.Writes.Setup(r=>r.CreateOrderAsync(f.Workspace,It.IsAny<NexFlow.Application.Features.Orders.DTOs.OrderRecord>(),It.IsAny<CancellationToken>()))
            .Returns(async(Guid w,NexFlow.Application.Features.Orders.DTOs.OrderRecord order,CancellationToken ct)=>{
                await f.Orders.CreateOrderAsync(w,order,ct);
                throw new OrderCatalogChangedException(OrderCatalogChange.PriceOrCurrency,"Synthetic boundary failure");
            });
        Assert.Contains("enviada",await f.Turn("envía el pedido","commit"));
        Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        Assert.Single(f.Writes.Invocations,i=>i.Method.Name=="CreateOrderAsync");
    }

    [Fact]
    public async Task Location_is_structured_optional_and_replay_cannot_reassign_it_to_another_workspace()
    {
        var db=new InMemoryFirestore();var f=new CommerceCertificationFixture(db.Db,db.Resources,Guid.NewGuid());await f.Seed();
        var root=db.Db.Collection("workspaces").Document(f.Workspace.ToString());
        await root.Collection("locations").Document("site").SetAsync(new {Name="Synthetic"});
        var state=await f.State();state.SelectedLocationId="site";await f.Context.SaveContextAsync(f.Workspace,"synthetic-client",state,default);
        await f.Turn("Quiero dos panes integrales","draft");await f.Turn("envía el pedido","commit");
        var order=Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));Assert.Equal("site",order.RequestedLocationId);
        var doc=root.Collection("orders").Document(order.Id);Assert.Equal("site",(await doc.GetSnapshotAsync()).GetValue<string>("RequestedLocationId"));
        order.RequestedLocationId="foreign-site";
        await Assert.ThrowsAsync<ConcurrencyException>(()=>f.Orders.CreateOrderAsync(f.Workspace,order,default));
        Assert.Null(await f.Orders.GetOrderByIdAsync(Guid.NewGuid(),order.Id,default));
        await doc.UpdateAsync(new Dictionary<string,object>{{"RequestedLocationId",Google.Cloud.Firestore.FieldValue.Delete}});
        Assert.Null((await f.Orders.GetOrderByIdAsync(f.Workspace,order.Id,default))!.RequestedLocationId);
    }

    [Theory]
    [InlineData("price")] [InlineData("currency")] [InlineData("location")]
    public async Task A_catalog_race_recovers_the_draft_and_requires_fresh_confirmation(string change)
    {
        var db=new InMemoryFirestore();var f=new CommerceCertificationFixture(db.Db,db.Resources,Guid.NewGuid());
        await f.Seed();await db.Db.Collection("workspaces").Document(f.Workspace.ToString()).Collection("locations").Document("site").SetAsync(new {Name="Synthetic"});
        var context=await f.State();context.SelectedLocationId="site";await f.Context.SaveContextAsync(f.Workspace,"synthetic-client",context,default);
        await f.Turn("Quiero dos panes integrales","draft");
        f.AfterReadProduct=async _=>{
            f.AfterReadProduct=null;
            var patch=new Dictionary<string,object>{[change=="price" ? "PriceMinorUnits" : change=="currency" ? "Currency" : "LocationScope"]=change=="price" ? 300L : change=="currency" ? "USD" : "SPECIFIC"};
            if(change=="location") patch["LocationIds"]=new[]{"other"};
            await db.Db.Collection("workspaces").Document(f.Workspace.ToString()).Collection("catalogItems").Document("bread").UpdateAsync(patch);
        };
        var response=await f.Turn("envía el pedido","commit");
        Assert.Contains("cambi",response,StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        var state=await f.State();Assert.Equal(2,Assert.Single(state.OrderDraftItems).Quantity);
        Assert.DoesNotContain("ORDER_COMMIT",state.CurrentStep ?? "");
        if(change!="location") {
            Assert.StartsWith("ORDER_CONFIRM:",state.CurrentStep);
            Assert.Contains(change=="price" ? "6.00" : "USD",response);
            Assert.Contains(change=="price" ? "precio de PEN 2.50 a PEN 3.00" : "moneda de PEN a USD",response);
            Assert.Equal(response,await f.Turn("envía el pedido","commit"));
            await f.Turn("envía el pedido","fresh-confirm");
            Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        }
    }
}
