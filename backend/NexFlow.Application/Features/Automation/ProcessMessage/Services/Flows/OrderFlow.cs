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
    private readonly IConversationCache _conversationCache;

    public OrderFlow(IOrderRepository orderRepository, INotificationService notificationService, IConversationCache conversationCache)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
        _conversationCache = conversationCache;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, string customerName, AiInterpretation interpretation, CancellationToken ct)
    {
        var context = await _conversationCache.GetContextAsync(workspaceId, phone, ct) ?? new ConversationContextDto();

        // 🔥 SPRINT 11: Cierre explícito del pedido y traspaso al humano
        if (interpretation.SearchTerm == "FINALIZAR_PEDIDO" || interpretation.SearchTerm?.Contains("FINALIZAR") == true)
        {
            if (!context.OrderDraftItems.Any())
                return "No tienes productos en tu carrito temporal. ¿Qué deseas pedir?";

            var orderItems = new List<OrderItemRecord>();
            foreach (var item in context.OrderDraftItems)
            {
                int qty = 1;
                string name = item;
                // Parseo básico de cantidades (Ej: "2x martillo")
                var parts = item.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Replace("x", "").Replace("X", ""), out int parsedQty))
                {
                    qty = parsedQty;
                    name = parts[1];
                }

                orderItems.Add(new OrderItemRecord
                {
                    ProductId = "GENERIC_ITEM",
                    ProductName = name.Trim(),
                    Quantity = qty,
                    UnitPriceMinorUnits = 0 // Sigue siendo cotización hasta que el humano lo revise
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
                "Intervención Requerida: Cotización de IA",
                $"El cliente {order.ConsumerName} cerró su lista con {orderItems.Count} ítems. Asume el control del chat y envíale los precios.",
                "/orders",
                ct);

            // Limpiamos el carrito y el objetivo para que la IA quede libre para otras consultas
            context.CurrentGoal = null;
            context.OrderDraftItems.Clear();
            await _conversationCache.SetContextAsync(workspaceId, phone, context, ct);

            return $"¡Excelente! He enviado tu lista de pedido. En este momento estoy transfiriendo el chat a un asesor humano para que confirme el stock y los precios exactos. Tu código de atención es {order.Id.Substring(0, 6)}.";
        }

        // 🔥 SPRINT 11: Modo "Carrito de Compras" (Multi-Turno)
        var rawItems = interpretation.SearchTerm?.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var newItems = new List<string>();

        foreach (var itemStr in rawItems)
        {
            if (!string.IsNullOrWhiteSpace(itemStr) && itemStr != "FINALIZAR_PEDIDO")
            {
                newItems.Add(itemStr.Trim());
            }
        }

        if (!newItems.Any() && !context.OrderDraftItems.Any())
        {
            return "No logré identificar los productos. ¿Podrías detallar tu pedido?";
        }

        context.OrderDraftItems.AddRange(newItems);
        context.CurrentGoal = "ORDER";
        await _conversationCache.SetContextAsync(workspaceId, phone, context, ct);

        var listText = string.Join("\n- ", context.OrderDraftItems);
        return $"Anotado. Hasta el momento tu lista tiene:\n- {listText}\n\n¿Deseas agregar algo más? (Si ya terminaste, escribe 'enviar pedido').";
    }
}