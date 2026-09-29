using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

public interface IOrderFlow
{
    Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, string customerName, string messageText, AiInterpretation interpretation, CancellationToken ct);
}

public class OrderFlow : IOrderFlow
{
    private readonly IOrderRepository _orderRepository;
    private readonly INotificationService _notificationService;
    private readonly IContextRecoveryService _contextStore;
    private readonly IOfferingService _offerings;

    public OrderFlow(IOrderRepository orderRepository, INotificationService notificationService, IContextRecoveryService contextStore, IOfferingService offerings)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
        _contextStore = contextStore;
        _offerings = offerings;
    }

    public async Task<string> ProcessAsync(Guid workspaceId, string phone, string conversationId, string customerName, string messageText, AiInterpretation interpretation, CancellationToken ct)
    {
        var context = await _contextStore.GetOrRecoverContextAsync(workspaceId, phone, ct);
        context.CurrentGoal = "ORDER";
        var response = await ProcessTurnAsync(workspaceId, phone, conversationId, customerName, messageText, interpretation, context, ct);
        await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
        return response;
    }

    private async Task<string> ProcessTurnAsync(Guid workspaceId, string phone, string conversationId, string customerName,
        string messageText, AiInterpretation interpretation, ConversationContextDto context, CancellationToken ct)
    {
        var command = Normalize(messageText).Trim(' ', '.', '!', '?', '¿', '¡');
        if (command is "cancelar" or "cancelar pedido" or "cancela el pedido")
        {
            CloseDraft(context);
            return "Pedido cancelado.";
        }
        if (command is "mostrar pedido" or "listar pedido" or "ver pedido" or "mi pedido")
            return Summary(context);

        if (command is "enviar pedido" or "finalizar pedido" or "finalizar" || interpretation.SearchTerm == "FINALIZAR_PEDIDO")
        {
            if (context.OrderDraftItems.Count == 0) return "Tu pedido está vacío. ¿Qué producto deseas agregar?";
            var order = new OrderRecord
            {
                ConversationId = conversationId,
                ConsumerPhone = phone,
                ConsumerName = string.IsNullOrWhiteSpace(interpretation.CustomerName) ? customerName : interpretation.CustomerName,
                Status = OrderStatus.PendingReview,
                Notes = "Solicitud pendiente de revisión por el negocio. Los precios corresponden al catálogo al enviarla.",
                Items = context.OrderDraftItems.Select(i => new OrderItemRecord
                {
                    ProductId = i.ProductId, ProductName = i.ProductName, Quantity = i.Quantity
                }).ToList()
            };
            try
            {
                // The repository validates every writer against the current catalog before persistence.
                await _orderRepository.CreateOrderAsync(workspaceId, order, ct);
            }
            catch (ArgumentException ex) { return ex.Message + " Corrige o quita el producto antes de enviar."; }

            CloseDraft(context);
            await _contextStore.SaveContextAsync(workspaceId, phone, context, ct);
            await _notificationService.NotifyAsync(workspaceId, "ORDERS", NotificationType.NewCommercialRequest,
                "Pedido recibido", $"{order.ConsumerName} envió una solicitud con {order.Items.Count} ítems para revisión.", "/orders", ct);
            return $"Tu solicitud de pedido fue enviada. El negocio la revisará. Código: {order.Id[..6]}.";
        }

        var remove = Regex.Match(command, @"^(?:quitar|quita|eliminar|elimina|sacar|saca)\s+(.+)$");
        if (remove.Success)
        {
            var name = remove.Groups[1].Value;
            var matches = context.OrderDraftItems.Where(i => Normalize(i.ProductName) == name || i.ProductId == name).ToList();
            if (matches.Count != 1) return "Indica el nombre exacto o ID del producto que quieres quitar.\n" + Summary(context);
            context.OrderDraftItems.Remove(matches[0]);
            return Summary(context);
        }

        var change = Regex.Match(command, @"^(?:cambiar|cambia|poner|pon)\s+(.+?)\s+a\s+(-?\d+)\s*(?:unidades)?$");
        if (change.Success)
        {
            if (!int.TryParse(change.Groups[2].Value, out var quantity) || quantity <= 0)
                return "La cantidad debe ser un entero mayor que cero.";
            var matches = context.OrderDraftItems.Where(i => Normalize(i.ProductName) == change.Groups[1].Value || i.ProductId == change.Groups[1].Value).ToList();
            if (matches.Count != 1) return "Indica el nombre exacto o ID del producto cuya cantidad quieres cambiar.\n" + Summary(context);
            var product = await _offerings.GetProductByIdAsync(workspaceId, matches[0].ProductId, ct);
            if (product == null) return "Ese producto ya no está disponible. Puedes quitarlo del pedido.";
            matches[0].Quantity = quantity;
            matches[0].ProductName = product.Name;
            return Summary(context);
        }

        // Commands must not become additions just because extraction omitted the operation.
        if (Regex.IsMatch(command, @"^(?:cambiar|cambia|poner|pon|quitar|quita|eliminar|elimina|sacar|saca|cancelar|mostrar|listar|ver)\b"))
            return "Puedes escribir 'cambiar PRODUCTO a 2', 'quitar PRODUCTO', 'mostrar pedido' o 'cancelar pedido'.";

        if (Regex.IsMatch(command, @"(?:^|\s)(?:-\d+|0+)(?:[xX]|\s|$)"))
            return "La cantidad debe ser un entero mayor que cero.";

        var input = interpretation.SearchTerm;
        if (string.IsNullOrWhiteSpace(input)) return Summary(context);
        var products = (await _offerings.GetProductsAsync(workspaceId, context.SelectedLocationId, null, ct)).ToList();
        var additions = new List<OrderDraftItem>();
        foreach (var raw in input.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(raw.Trim(), @"^(?:(-?\d+)\s*[xX]?\s+)?(.+)$");
            var qty = 1;
            if (match.Groups[1].Success && (!int.TryParse(match.Groups[1].Value, out qty) || qty <= 0))
                return "La cantidad debe ser un entero mayor que cero.";
            var name = Normalize(match.Groups[2].Value);
            var matches = products.Where(p => Normalize(p.Name) == name || p.Id.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1)
                return matches.Count == 0 ? $"No encontré un producto activo llamado '{match.Groups[2].Value}'. Indica el nombre exacto o ID del catálogo."
                    : "Hay varios productos con ese nombre. Indica su ID:\n" + string.Join("\n", matches.Select(p => $"- {p.Name}: {p.Id}"));
            additions.Add(new OrderDraftItem { ProductId = matches[0].Id, ProductName = matches[0].Name, Quantity = qty });
        }
        // Validate the complete turn before mutating the durable draft.
        foreach (var group in additions.GroupBy(i => i.ProductId))
        {
            var current = context.OrderDraftItems.FirstOrDefault(i => i.ProductId == group.Key)?.Quantity ?? 0;
            if (group.Sum(i => (long)i.Quantity) + current > int.MaxValue) return "La cantidad indicada es demasiado grande.";
        }
        foreach (var item in additions)
        {
            var existing = context.OrderDraftItems.FirstOrDefault(i => i.ProductId == item.ProductId);
            if (existing == null) context.OrderDraftItems.Add(item);
            else existing.Quantity += item.Quantity;
        }
        return Summary(context);
    }

    private static void CloseDraft(ConversationContextDto context)
    {
        context.CurrentGoal = null;
        context.CurrentStep = null;
        context.OrderDraftItems.Clear();
        context.MissingFields.Clear();
        context.LastQuestion = null;
    }

    private static string Summary(ConversationContextDto context) =>
        (context.OrderDraftItems.Count == 0 ? "Tu pedido está vacío." : "Pedido actual:\n" + string.Join("\n", context.OrderDraftItems.Select(i => $"- {i.Quantity}x {i.ProductName}")))
        + "\nPuedes agregar productos, cambiar PRODUCTO a CANTIDAD, quitar PRODUCTO, mostrar pedido, cancelar pedido o enviar pedido.";

    private static string Normalize(string text) => string.Concat(text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).Normalize(NormalizationForm.FormC);
}
