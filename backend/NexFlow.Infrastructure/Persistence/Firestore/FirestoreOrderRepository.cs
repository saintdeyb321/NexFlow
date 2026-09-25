using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Domain.Enums;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreOrderRepository : IOrderRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreOrderRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    private CollectionReference GetCollection(Guid workspaceId) =>
        _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("orders");

    public async Task<OrderRecord> CreateOrderAsync(Guid workspaceId, OrderRecord order, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(order.Id);

        var data = new Dictionary<string, object>
        {
            { "Id", order.Id },
            { "ConversationId", order.ConversationId },
            { "ConsumerPhone", order.ConsumerPhone },
            { "ConsumerName", order.ConsumerName },
            { "Status", order.Status.ToString() },
            { "Currency", order.Currency },
            { "TotalAmountMinorUnits", order.TotalAmountMinorUnits },
            { "Notes", order.Notes ?? "" },
            { "CreatedAt", DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc) },
            { "UpdatedAt", DateTime.SpecifyKind(order.UpdatedAt, DateTimeKind.Utc) },
            { "Items", order.Items.Select(i => new Dictionary<string, object>
                {
                    { "ProductId", i.ProductId },
                    { "ProductName", i.ProductName },
                    { "Quantity", i.Quantity },
                    { "UnitPriceMinorUnits", i.UnitPriceMinorUnits }
                }).ToList()
            }
        };

        await docRef.SetAsync(data, cancellationToken: cancellationToken);
        return order;
    }

    public async Task<OrderRecord?> GetOrderByIdAsync(Guid workspaceId, string orderId, CancellationToken cancellationToken)
    {
        var snapshot = await GetCollection(workspaceId).Document(orderId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists) return null;

        return MapToOrderRecord(snapshot);
    }

    public async Task<IEnumerable<OrderRecord>> GetOrdersAsync(Guid workspaceId, OrderStatus? status, CancellationToken cancellationToken)
    {
        var query = GetCollection(workspaceId).OrderByDescending("CreatedAt");

        if (status.HasValue)
        {
            query = GetCollection(workspaceId).WhereEqualTo("Status", status.Value.ToString()).OrderByDescending("CreatedAt");
        }

        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToOrderRecord).ToList();
    }

    public async Task UpdateOrderStatusAsync(Guid workspaceId, string orderId, OrderStatus newStatus, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(orderId);
        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            { "Status", newStatus.ToString() },
            { "UpdatedAt", DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc) }
        }, cancellationToken: cancellationToken);
    }

    private static OrderRecord MapToOrderRecord(DocumentSnapshot doc)
    {
        var record = new OrderRecord
        {
            Id = doc.GetValue<string>("Id"),
            ConversationId = doc.GetValue<string>("ConversationId"),
            ConsumerPhone = doc.GetValue<string>("ConsumerPhone"),
            ConsumerName = doc.GetValue<string>("ConsumerName"),
            Status = Enum.Parse<OrderStatus>(doc.GetValue<string>("Status")),
            Currency = doc.GetValue<string>("Currency"),
            TotalAmountMinorUnits = doc.GetValue<long>("TotalAmountMinorUnits"),
            Notes = doc.TryGetValue("Notes", out string notes) ? notes : null,
            CreatedAt = doc.GetValue<DateTime>("CreatedAt"),
            UpdatedAt = doc.GetValue<DateTime>("UpdatedAt")
        };

        if (doc.TryGetValue("Items", out List<object> itemsRaw))
        {
            foreach (var itemRaw in itemsRaw)
            {
                if (itemRaw is Dictionary<string, object> itemDict)
                {
                    record.Items.Add(new OrderItemRecord
                    {
                        ProductId = itemDict.ContainsKey("ProductId") ? itemDict["ProductId"].ToString()! : "",
                        ProductName = itemDict.ContainsKey("ProductName") ? itemDict["ProductName"].ToString()! : "",
                        Quantity = itemDict.ContainsKey("Quantity") ? Convert.ToInt32(itemDict["Quantity"]) : 0,
                        UnitPriceMinorUnits = itemDict.ContainsKey("UnitPriceMinorUnits") ? Convert.ToInt64(itemDict["UnitPriceMinorUnits"]) : 0
                    });
                }
            }
        }

        return record;
    }
}