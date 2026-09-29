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
    public async Task<IActionResult> GetOrders([FromQuery] OrderStatus? status, CancellationToken cancellationToken, [FromQuery] int limit = 50)
    {
        if (!await HasAccessAsync(cancellationToken)) return StatusCode(403, "Módulo de Pedidos no contratado.");

        if (limit is < 1 or > 100) return BadRequest(new { code = "Pagination.Invalid", message = "El límite debe estar entre 1 y 100." });
        var orders = await _orderRepository.GetOrdersAsync(WorkspaceId, status, cancellationToken, limit);
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

        try { await _orderRepository.CreateOrderAsync(WorkspaceId, order, cancellationToken); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }

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
        if (!Enum.IsDefined(request.Status)) return BadRequest(new { code = "Order.InvalidStatus", message = "Estado inválido." });

        var order = await _orderRepository.GetOrderByIdAsync(WorkspaceId, id, cancellationToken);
        if (order == null) return NotFound(new { message = "Pedido no encontrado." });

        if (!OrderLifecycle.CanTransition(order.Status, request.Status))
            return Conflict(new { code = "Order.InvalidTransition", message = "La transición del pedido no es válida." });
        await _orderRepository.UpdateOrderStatusAsync(WorkspaceId, id, order.Status, request.Status, cancellationToken);

        return Ok(new { message = "Estado actualizado exitosamente." });
    }

    [HttpPut("{id}/amount")]
    public async Task<IActionResult> UpdateAmount(string id, [FromBody] UpdateOrderAmountRequest request, CancellationToken cancellationToken)
    {
        if (!await HasAccessAsync(cancellationToken)) return StatusCode(403, "Módulo de Pedidos no contratado.");

        if (request.TotalAmountMinorUnits < 0)
            return BadRequest(new { message = "El monto de la cotización no puede ser negativo." });

        var order = await _orderRepository.GetOrderByIdAsync(WorkspaceId, id, cancellationToken);
        if (order == null) return NotFound(new { message = "Pedido no encontrado." });

        if (string.IsNullOrWhiteSpace(order.Currency))
            return BadRequest(new { message = "No se puede fijar un total sin una moneda única." });
        var factualTotal = order.Items.Sum(i => i.SubtotalMinorUnits);
        if (request.TotalAmountMinorUnits != factualTotal)
            return BadRequest(new { message = "El total debe corresponder a los precios registrados de los ítems." });
        await _orderRepository.UpdateOrderAmountAsync(WorkspaceId, id, factualTotal, cancellationToken);

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
