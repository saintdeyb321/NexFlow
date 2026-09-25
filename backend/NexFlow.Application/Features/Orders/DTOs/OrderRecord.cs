using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Orders.DTOs;

public class OrderRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string ConsumerPhone { get; set; } = string.Empty;
    public string ConsumerName { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.PendingReview;
    public string Currency { get; set; } = "PEN";
    public long TotalAmountMinorUnits { get; set; }

    public List<OrderItemRecord> Items { get; set; } = new();

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class OrderItemRecord
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long UnitPriceMinorUnits { get; set; }
    public long SubtotalMinorUnits => Quantity * UnitPriceMinorUnits;
}