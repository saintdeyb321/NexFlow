using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Application.Features.Notifications;
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
        return activeModules.Contains("ORDERS");
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

        // 🔥 SPRINT 11: Forzamos a que todo pedido nuevo entre como cotización (0.00)
        foreach (var item in order.Items)
        {
            item.UnitPriceMinorUnits = 0;
        }
        order.TotalAmountMinorUnits = 0;

        await _orderRepository.CreateOrderAsync(WorkspaceId, order, cancellationToken);

        // 🔥 SPRINT 11: Se envía al módulo "ORDERS", no a "CATALOG"
        await _notificationService.NotifyAsync(
            WorkspaceId,
            "ORDERS",
            NotificationType.NewCommercialRequest,
            "Nueva Solicitud/Cotización Registrada",
            $"Se ha recibido una solicitud de {order.ConsumerName} con {order.Items.Count} ítems por revisar.",
            "/orders",
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

    [HttpPut("{id}/amount")]
    public async Task<IActionResult> UpdateAmount(string id, [FromBody] UpdateOrderAmountRequest request, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(cancellationToken)) return StatusCode(403, "Módulo de Pedidos no contratado.");

        // 🔥 SPRINT 11: Bloqueamos montos negativos
        if (request.TotalAmountMinorUnits < 0)
            return BadRequest(new { message = "El monto de la cotización no puede ser negativo." });

        var order = await _orderRepository.GetOrderByIdAsync(WorkspaceId, id, cancellationToken);
        if (order == null) return NotFound(new { message = "Pedido no encontrado." });

        await _orderRepository.UpdateOrderAmountAsync(WorkspaceId, id, request.TotalAmountMinorUnits, cancellationToken);

        return Ok(new { message = "Monto actualizado exitosamente." });
    }
}

public class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; set; }
}

public class UpdateOrderAmountRequest
{
    public long TotalAmountMinorUnits { get; set; }
}