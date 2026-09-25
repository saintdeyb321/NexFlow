using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Application.Features.Notifications; // 🔥 Para las notificaciones
using NexFlow.Domain.Enums;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/orders")]
[Authorize(Policy = "WorkspaceMember")]
public class OrdersController : ControllerBase
{
    private readonly IOrderRepository _orderRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;
    private readonly INotificationService _notificationService;

    public OrdersController(
        IOrderRepository orderRepository,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService,
        INotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
        _notificationService = notificationService;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    private async Task<bool> HasAccessAsync(CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        // 🔥 Permitimos acceso si tienen ORDERS o CATALOG (ya que Orders es la evolución de Catalog)
        return activeModules.Contains("ORDERS") || activeModules.Contains("CATALOG");
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] OrderStatus? status, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(cancellationToken)) return StatusCode(403, "Módulo de Pedidos no contratado.");

        var orders = await _orderRepository.GetOrdersAsync(WorkspaceId, status, cancellationToken);
        return Ok(orders);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(string id, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(cancellationToken)) return StatusCode(403, "Módulo de Pedidos no contratado.");

        var order = await _orderRepository.GetOrderByIdAsync(WorkspaceId, id, cancellationToken);
        if (order == null) return NotFound(new { message = "Pedido no encontrado." });

        return Ok(order);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] OrderRecord order, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(cancellationToken)) return StatusCode(403, "Módulo de Pedidos no contratado.");

        order.Id = Guid.NewGuid().ToString();
        order.CreatedAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;
        order.Status = OrderStatus.PendingReview;

        // Calculamos el total de forma segura desde el backend
        order.TotalAmountMinorUnits = order.Items.Sum(i => i.Quantity * i.UnitPriceMinorUnits);

        await _orderRepository.CreateOrderAsync(WorkspaceId, order, cancellationToken);

        // 🔥 Notificamos al Dashboard instantáneamente
        await _notificationService.NotifyAsync(
            WorkspaceId,
            "CATALOG",
            NotificationType.NewCommercialRequest,
            "Nuevo Pedido Registrado",
            $"Se ha recibido un pedido de {order.ConsumerName} por {order.Currency} {(order.TotalAmountMinorUnits / 100.0):0.00}.",
            "/orders", // Redirige a la futura vista de pedidos en el frontend
            cancellationToken);

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(cancellationToken)) return StatusCode(403, "Módulo de Pedidos no contratado.");

        var order = await _orderRepository.GetOrderByIdAsync(WorkspaceId, id, cancellationToken);
        if (order == null) return NotFound(new { message = "Pedido no encontrado." });

        await _orderRepository.UpdateOrderStatusAsync(WorkspaceId, id, request.Status, cancellationToken);

        return Ok(new { message = "Estado actualizado exitosamente." });
    }
}

public class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; set; }
}