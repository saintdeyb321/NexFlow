using Google.Cloud.Firestore;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;

namespace NexFlow.Tests.Concierge;

// Same production services against either the explicitly supplied Emulator or SDK fake.
internal sealed class CommerceCertificationFixture
{
    public Guid Workspace { get; }
    public FirestoreDb Db { get; }
    public FirestoreCatalogRepository Catalog { get; }
    public FirestoreOrderRepository Orders { get; }
    public IContextRecoveryService Context { get; }
    public Mock<INotificationService> Notifications { get; } = new();
    public Mock<IEntitlementService> Access { get; } = new();
    public Mock<IOrderRepository> Writes { get; } = new(MockBehavior.Strict);
    public IOfferingService Offerings { get; }
    public Func<Task>? BeforeCreate { get; set; }
    public Func<ProductDto, Task>? AfterReadProduct { get; set; }
    public bool Licensed { get; set; } = true;
    public ILocationRepository Locations { get; }
    public OrderFlow Flow => new(Writes.Object, Notifications.Object, Context, Offerings, Access.Object, locations: Locations);
    public CommerceCertificationFixture(FirestoreDb db, IReservationResourceCoordinator resources, Guid workspace)
    {
        Db=db;Workspace=workspace;Catalog=new(db,resources);Locations=new FirestoreLocationRepository(db,resources);
        var actual=new OfferingService(Catalog);
        var offerings=new Mock<IOfferingService>(MockBehavior.Strict);
        offerings.Setup(o=>o.GetProductsAsync(Workspace,It.IsAny<string?>(),It.IsAny<string?>(),It.IsAny<CancellationToken>()))
            .Returns((Guid w,string? l,string? c,CancellationToken ct)=>actual.GetProductsAsync(w,l,c,ct));
        offerings.Setup(o=>o.GetProductByIdAsync(Workspace,It.IsAny<string>(),It.IsAny<CancellationToken>()))
            .Returns(async (Guid w,string id,CancellationToken ct)=> {
                var p=await actual.GetProductByIdAsync(w,id,ct);
                if(p!=null && AfterReadProduct!=null) await AfterReadProduct(p);
                return p;
            });
        Offerings=offerings.Object;Orders=new(db,Offerings);
        Context=new ContextRecoveryService(Mock.Of<IConversationCache>(),new FirestoreConversationStateRepository(db));
        Access.Setup(a=>a.GetAvailableModuleCodesAsync(Workspace,It.IsAny<CancellationToken>())).ReturnsAsync(()=>Licensed ? new[]{"CATALOG","ORDERS"} : []);
        Writes.Setup(r=>r.GetOrderByIdAsync(Workspace,It.IsAny<string>(),It.IsAny<CancellationToken>()))
            .Returns((Guid w,string id,CancellationToken ct)=>Orders.GetOrderByIdAsync(w,id,ct));
        Writes.Setup(r=>r.CreateOrderAsync(Workspace,It.IsAny<OrderRecord>(),It.IsAny<CancellationToken>()))
            .Returns(async (Guid w,OrderRecord order,CancellationToken ct)=>{
                if(BeforeCreate!=null) await BeforeCreate();
                return await Orders.CreateOrderAsync(w,order,ct);
            });
    }
    public Task Seed(long price=250,string currency="PEN")=>Catalog.SaveItemAsync(Workspace,new ProductDto{Id="bread",Name="Pan integral",PriceMinorUnits=price,Currency=currency},true,default);
    public Task<string> Turn(string text,string source)=>Flow.ProcessAsync(Workspace,"synthetic-client","synthetic-chat","Synthetic",text,new(){Intent=ConversationIntent.Order},source,default);
    public Task<ConversationContextDto> State()=>Context.GetOrRecoverContextAsync(Workspace,"synthetic-client",default);
    public Task<string> Review()=>Turn("finalizar","consent-review");
}
