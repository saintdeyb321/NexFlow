using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.Business.Offerings;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreOrderRepository : IOrderRepository
{
    private readonly FirestoreDb _firestoreDb;
    private readonly IOfferingService _offerings;

    public FirestoreOrderRepository(FirestoreDb firestoreDb, IOfferingService offerings)
    {
        _firestoreDb = firestoreDb;
        _offerings = offerings;
    }

    private CollectionReference GetCollection(Guid workspaceId) =>
        _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("orders");

    public async Task<OrderRecord> CreateOrderAsync(Guid workspaceId, OrderRecord order, CancellationToken cancellationToken)
    {
        if (order.Items == null || order.Items.Count == 0)
            throw new ArgumentException("El pedido debe contener productos.");
        foreach (var item in order.Items)
        {
            if (item.Quantity <= 0 || string.IsNullOrWhiteSpace(item.ProductId))
                throw new ArgumentException("Cada producto debe tener un ID de catálogo y cantidad mayor que cero.");
            var product = await _offerings.GetProductByIdAsync(workspaceId, item.ProductId, cancellationToken);
            if (product == null)
                throw new ArgumentException($"El producto '{item.ProductName}' no está disponible en el catálogo.");
            if (product.PriceMinorUnits < 0 || string.IsNullOrWhiteSpace(product.Currency))
                throw new ArgumentException($"El producto '{product.Name}' no tiene un precio/moneda válido.");
            item.ProductId = product.Id;
            item.ProductName = product.Name;
            item.UnitPriceMinorUnits = product.PriceMinorUnits;
            item.Currency = product.Currency;
            try { _ = item.SubtotalMinorUnits; }
            catch (OverflowException) { throw new ArgumentException("El importe del producto supera el máximo permitido."); }
        }
        var currencies = order.Items.Select(i => i.Currency).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        order.Currency = currencies.Count == 1 ? currencies[0] : null;
        try { order.TotalAmountMinorUnits = order.Currency == null ? null : order.Items.Sum(i => i.SubtotalMinorUnits); }
        catch (OverflowException) { throw new ArgumentException("El importe del pedido supera el máximo permitido."); }
        order.Status = OrderStatus.PendingReview;
        if (order.Currency == null) order.Notes = (order.Notes + " Total pendiente: los productos tienen monedas diferentes.").Trim();
        var docRef = GetCollection(workspaceId).Document(order.Id);

        var data = new Dictionary<string, object>
        {
            { "Id", order.Id },
            { "ConversationId", order.ConversationId },
            { "ConsumerPhone", order.ConsumerPhone },
            { "ConsumerName", order.ConsumerName },
            { "Status", order.Status.ToString() },
            { "Currency", order.Currency! },
            { "TotalAmountMinorUnits", order.TotalAmountMinorUnits! },
            { "Notes", order.Notes ?? "" },
            { "CreatedAt", DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc) },
            { "UpdatedAt", DateTime.SpecifyKind(order.UpdatedAt, DateTimeKind.Utc) },
            { "Items", order.Items.Select(i => new Dictionary<string, object>
                {
                    { "ProductId", i.ProductId },
                    { "ProductName", i.ProductName },
                    { "Quantity", i.Quantity },
                    { "UnitPriceMinorUnits", i.UnitPriceMinorUnits },
                    { "Currency", i.Currency! }
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

    public async Task UpdateOrderAmountAsync(Guid workspaceId, string orderId, long totalAmountMinorUnits, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(orderId);
        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            { "TotalAmountMinorUnits", totalAmountMinorUnits },
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
            TotalAmountMinorUnits = doc.GetValue<long?>("TotalAmountMinorUnits"),
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
                        UnitPriceMinorUnits = itemDict.ContainsKey("UnitPriceMinorUnits") ? Convert.ToInt64(itemDict["UnitPriceMinorUnits"]) : 0,
                        Currency = itemDict.TryGetValue("Currency", out var currency) ? currency?.ToString() : record.Currency
                    });
                }
            }
        }

        return record;
    }
}
