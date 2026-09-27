using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.AI.Interpretation;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

public interface IOrderFlow
{
    Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, string customerName, AiInterpretation interpretation, CancellationToken ct);
}

public class OrderFlow : IOrderFlow
{
    private readonly IOrderRepository _orderRepository;
    private readonly INotificationService _notificationService;

    public OrderFlow(IOrderRepository orderRepository, INotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, string customerName, AiInterpretation interpretation, CancellationToken ct)
    {
        var rawItems = interpretation.SearchTerm?.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var orderItems = new List<OrderItemRecord>();

        foreach (var itemStr in rawItems)
        {
            if (string.IsNullOrWhiteSpace(itemStr)) continue;

            orderItems.Add(new OrderItemRecord
            {
                ProductId = "GENERIC_ITEM",
                ProductName = itemStr.Trim(),
                Quantity = 1,
                UnitPriceMinorUnits = 0
            });
        }

        if (!orderItems.Any())
        {
            return "No logré identificar los productos. ¿Podrías detallar tu pedido o consulta nuevamente?";
        }

        var order = new OrderRecord
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            ConsumerPhone = phone,
            ConsumerName = string.IsNullOrWhiteSpace(interpretation.CustomerName) ? customerName : interpretation.CustomerName,
            Status = OrderStatus.PendingReview,
            Currency = "PEN",
            TotalAmountMinorUnits = 0,
            Items = orderItems,
            // 🔥 SPRINT 10: Limpiamos la falsa ilusión de compra automática. Es una cotización.
            Notes = "Cotización solicitada vía IA. Requiere intervención humana para fijar precios y confirmar stock.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _orderRepository.CreateOrderAsync(workspaceId, order, ct);

        // 🔥 SPRINT 10: Notificación explícita obligando a la intervención humana
        await _notificationService.NotifyAsync(
            workspaceId,
            "ORDERS",
            NotificationType.NewCommercialRequest,
            "Intervención Requerida: Cotización de IA",
            $"El cliente {order.ConsumerName} solicitó {orderItems.Count} ítems. Asume el control del chat, revisa el stock y envíale los precios.",
            "/orders",
            ct);

        return $"He registrado tu solicitud de productos. En este momento estoy transfiriendo el chat a un asesor humano para que confirme el stock y los precios exactos. Tu código de atención es {order.Id.Substring(0, 6)}.";
    }
}