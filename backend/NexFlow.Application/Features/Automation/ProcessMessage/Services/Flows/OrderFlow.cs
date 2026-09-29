using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

public interface IOrderFlow
{
    Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, string customerName, AiInterpretation interpretation, CancellationToken ct);
}

public class OrderFlow : IOrderFlow
{
    private readonly IOrderRepository _orderRepository;
    private readonly INotificationService _notificationService;
    private readonly IContextRecoveryService _contextStore;

    public OrderFlow(IOrderRepository orderRepository, INotificationService notificationService, IContextRecoveryService contextStore)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
        _contextStore = contextStore;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, string customerName, AiInterpretation interpretation, CancellationToken ct)
    {
        var context = await _contextStore.GetOrRecoverContextAsync(workspaceId, phone, ct);
        var response = await ProcessTurnAsync(workspaceId, phone, conversationId, customerName, interpretation, context, ct);
        await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
        return response;
    }

    private async Task<string> ProcessTurnAsync(Guid workspaceId, string phone, string conversationId, string customerName,
        AiInterpretation interpretation, ConversationContextDto context, CancellationToken ct)
    {
        context.CurrentGoal = "ORDER";

        if (interpretation.SearchTerm == "FINALIZAR_PEDIDO" || interpretation.SearchTerm?.Contains("FINALIZAR") == true)
        {
            if (!context.OrderDraftItems.Any())
                return "No tienes productos en tu carrito temporal. ¿Qué deseas pedir?";

            var orderItems = new List<OrderItemRecord>();
            foreach (var item in context.OrderDraftItems)
            {
                orderItems.Add(new OrderItemRecord
                {
                    ProductId = "GENERIC_ITEM",
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPriceMinorUnits = 0
                });
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
                Notes = "Cotización solicitada vía IA. Requiere intervención humana para fijar precios y confirmar stock.",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _orderRepository.CreateOrderAsync(workspaceId, order, ct);

            await _notificationService.NotifyAsync(
                workspaceId,
                "ORDERS",
                NotificationType.NewCommercialRequest,
                "Cotización Recibida",
                $"El cliente {order.ConsumerName} cerró su lista con {orderItems.Count} ítems. Entra al chat para enviar precios.",
                "/orders",
                ct);

            context.CurrentGoal = null;
            context.OrderDraftItems.Clear();

            return $"¡Excelente! He enviado tu lista de pedido. En este momento estoy transfiriendo el chat a un asesor humano para que confirme el stock y los precios exactos. Tu código de atención es {order.Id.Substring(0, 6)}.";
        }

        // 🔥 SPRINT 06: Múltiples turnos y extracción precisa
        var rawItems = interpretation.SearchTerm?.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

        foreach (var itemStr in rawItems)
        {
            if (!string.IsNullOrWhiteSpace(itemStr) && itemStr != "FINALIZAR_PEDIDO")
            {
                int qty = 1;
                string name = itemStr.Trim();

                var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Replace("x", "").Replace("X", ""), out int parsedQty))
                {
                    qty = parsedQty;
                    name = parts[1].Trim();
                }

                // Si ya existe el producto, sumamos cantidad, sino lo agregamos.
                var existingItem = context.OrderDraftItems.FirstOrDefault(i => i.ProductName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existingItem != null)
                {
                    existingItem.Quantity += qty;
                }
                else
                {
                    context.OrderDraftItems.Add(new OrderDraftItem { ProductName = name, Quantity = qty });
                }
            }
        }

        if (!context.OrderDraftItems.Any())
        {
            return "No logré identificar los productos. ¿Podrías detallar tu pedido?";
        }

        var listText = string.Join("\n", context.OrderDraftItems.Select(i => $"- {i.Quantity}x {i.ProductName}"));
        return $"Anotado. Hasta el momento tu lista tiene:\n{listText}\n\n¿Deseas agregar algo más? (Si ya terminaste, escribe 'enviar pedido').";
    }
}