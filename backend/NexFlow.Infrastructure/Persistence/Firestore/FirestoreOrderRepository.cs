using NexFlow.Domain.Exceptions;
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
        var docRef = GetCollection(workspaceId).Document(order.Id);
        var existing = await docRef.GetSnapshotAsync(cancellationToken);
        if (existing.Exists) return ValidateReplay(MapToOrderRecord(existing), order);
        foreach (var item in order.Items)
        {
            if (item.Quantity <= 0 || string.IsNullOrWhiteSpace(item.ProductId))
                throw new ArgumentException("Cada producto debe tener un ID de catálogo y cantidad mayor que cero.");
            var product = await _offerings.GetProductByIdAsync(workspaceId, item.ProductId, cancellationToken);
            if (product == null)
                throw new ArgumentException($"El producto '{item.ProductName}' no está disponible en el catálogo.");
            if (order.RequestedLocationId != null && !string.Equals(product.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase)
                && (!string.Equals(product.LocationScope, "SPECIFIC", StringComparison.OrdinalIgnoreCase) || !product.LocationIds.Contains(order.RequestedLocationId)))
                throw new ArgumentException("Un producto no se ofrece en la sede seleccionada.");
            if (product.PriceMinorUnits < 0 || string.IsNullOrWhiteSpace(product.Currency))
                throw new ArgumentException($"El producto '{product.Name}' no tiene un precio/moneda válido.");
            if (order.ExpectedCatalog != null && (!order.ExpectedCatalog.TryGetValue(item.ProductId,out var expected)
                || expected.Name != product.Name || expected.PriceMinorUnits != product.PriceMinorUnits || expected.Currency != product.Currency))
                throw new ConcurrencyException("El catálogo cambió después de mostrar el subtotal. Revisa los importes antes de confirmar.");
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

        return await _firestoreDb.RunTransactionAsync(async tx =>
        {
            var current = await tx.GetSnapshotAsync(docRef, cancellationToken);
            if (current.Exists) return ValidateReplay(MapToOrderRecord(current), order);
            if (order.RequestedLocationId != null)
            {
                var location = await tx.GetSnapshotAsync(docRef.Parent.Parent!.Collection("locations").Document(order.RequestedLocationId), cancellationToken);
                if (!location.Exists) throw new ArgumentException("La sede seleccionada ya no está disponible. Elige otra sede antes de enviar.");
            }
            // Fence the authorized snapshots against catalog edits in the same
            // Firestore transaction as creation. A changed price needs a fresh request,
            // never a silent recalculation of an existing order.
            foreach (var item in order.Items)
            {
                var product = await tx.GetSnapshotAsync(docRef.Parent.Parent!.Collection("catalogItems").Document(item.ProductId), cancellationToken);
                if (!product.Exists || product.GetValue<string>("Type") != "PRODUCT" || !product.GetValue<bool>("IsActive"))
                    throw new ArgumentException("Un producto del pedido ya no está disponible en el catálogo.");
                if (order.RequestedLocationId != null && product.TryGetValue<string>("LocationScope", out var scope)
                    && !scope.Equals("ALL", StringComparison.OrdinalIgnoreCase)
                    && (!scope.Equals("SPECIFIC", StringComparison.OrdinalIgnoreCase) || !product.TryGetValue<List<string>>("LocationIds", out var ids) || !ids.Contains(order.RequestedLocationId)))
                    throw new ConcurrencyException("El catálogo cambió de sede al enviar el pedido. Revisa la solicitud.");
                if (product.GetValue<string>("Name") != item.ProductName
                    || product.GetValue<long>("PriceMinorUnits") != item.UnitPriceMinorUnits
                    || product.GetValue<string>("Currency") != item.Currency)
                    throw new ConcurrencyException("El catálogo cambió al enviar el pedido. Revisa sus precios y vuelve a enviarlo.");
            }
            tx.Create(docRef, data);
            return order;
        }, cancellationToken: cancellationToken);
    }

    private static OrderRecord ValidateReplay(OrderRecord existing, OrderRecord requested)
    {
        if (existing.ConsumerPhone != requested.ConsumerPhone || existing.ConversationId != requested.ConversationId ||
            !existing.Items.Select(i => (i.ProductId, i.Quantity)).OrderBy(i => i.ProductId, StringComparer.Ordinal)
                .SequenceEqual(requested.Items.Select(i => (i.ProductId, i.Quantity)).OrderBy(i => i.ProductId, StringComparer.Ordinal)))
            throw new ConcurrencyException("Order identity was reused for different content.");
        return existing;
    }

    public async Task<OrderRecord?> GetOrderByIdAsync(Guid workspaceId, string orderId, CancellationToken cancellationToken)
    {
        var snapshot = await GetCollection(workspaceId).Document(orderId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists) return null;

        return MapToOrderRecord(snapshot);
    }

    public async Task<IEnumerable<OrderRecord>> GetOrdersAsync(Guid workspaceId, OrderStatus? status, CancellationToken cancellationToken, int limit = 50)
    {
        var query = GetCollection(workspaceId).OrderByDescending("CreatedAt");

        if (status.HasValue)
        {
            query = GetCollection(workspaceId).WhereEqualTo("Status", status.Value.ToString()).OrderByDescending("CreatedAt");
        }

        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var snapshot = await query.Limit(limit).GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToOrderRecord).ToList();
    }

    public async Task UpdateOrderStatusAsync(Guid workspaceId, string orderId, OrderStatus expectedStatus, OrderStatus newStatus, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(orderId);
        await _firestoreDb.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(docRef, cancellationToken);
            if (!snapshot.Exists) throw new KeyNotFoundException("Pedido no encontrado.");
            var current = Enum.Parse<OrderStatus>(snapshot.GetValue<string>("Status"));
            if (current != expectedStatus || !OrderLifecycle.CanTransition(current, newStatus))
                throw new ConcurrencyException("El estado cambió o la transición del pedido no es válida.");
            transaction.Update(docRef, new Dictionary<string, object>
            {
                ["Status"] = newStatus.ToString(), ["UpdatedAt"] = DateTime.UtcNow
            });
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
