using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class CommerceLocationRecoveryTests
{
    [Fact]
    public async Task Deleted_selected_site_cannot_be_silently_dropped_by_submit_or_quantity_edit()
    {
        var db=new InMemoryFirestore();var f=new CommerceCertificationFixture(db.Db,db.Resources,Guid.NewGuid());await f.Seed();
        var root=db.Db.Collection("workspaces").Document(f.Workspace.ToString());var site=root.Collection("locations").Document("old");
        await site.SetAsync(new {Name="Synthetic"});await root.Collection("locations").Document("new").SetAsync(new {Name="Another synthetic"});
        var context=await f.State();context.SelectedLocationId="old";await f.Context.SaveContextAsync(f.Workspace,"synthetic-client",context,default);
        await f.Turn("Quiero dos panes integrales","draft");await f.Review();f.BeforeCreate=()=>site.DeleteAsync();
        await f.Turn("Confirmar","first");
        await f.Turn("Confirmar","second");Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        await f.Turn("mejor cuatro panes integrales","edit");Assert.Equal("ORDER_LOCATION",(await f.State()).CurrentStep);
        await f.Turn("Confirmar","third");Assert.Empty(await f.Orders.GetOrdersAsync(f.Workspace,null,default));
        var state=await f.State();
        await f.Flow.ProcessPlannedAsync(f.Workspace,"synthetic-client","synthetic-chat","Synthetic","another site",new(){
            Intent=NexFlow.Domain.Enums.ConversationIntent.Order,
            OrderProposal=new(f.Workspace,"move",state.StateVersion,OrderTurnKind.Location,Array.AsReadOnly(Array.Empty<OrderEdit>()),"new")},"move",DialogueAct.Correction,default);
        await f.Turn("finalizar","new-site-review");await f.Turn("Confirmar","fresh");
        Assert.Equal("new",Assert.Single(await f.Orders.GetOrdersAsync(f.Workspace,null,default)).RequestedLocationId);
    }
}
