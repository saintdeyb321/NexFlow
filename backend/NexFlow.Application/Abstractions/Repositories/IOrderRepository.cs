using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IOrderRepository
{
    Task<OrderRecord> CreateOrderAsync(Guid workspaceId, OrderRecord order, CancellationToken cancellationToken);
    Task<OrderRecord?> GetOrderByIdAsync(Guid workspaceId, string orderId, CancellationToken cancellationToken);
    Task<IEnumerable<OrderRecord>> GetOrdersAsync(Guid workspaceId, OrderStatus? status, CancellationToken cancellationToken);
    Task UpdateOrderStatusAsync(Guid workspaceId, string orderId, OrderStatus newStatus, CancellationToken cancellationToken);
}