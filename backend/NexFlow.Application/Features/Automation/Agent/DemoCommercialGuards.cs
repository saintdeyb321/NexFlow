using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Orders.DTOs;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Agent;

// Independent of AI decisions, scope and prompt. Every mutation in the existing
// registered engine is blocked; reads delegate to the same hardened engine.
public sealed class DemoReservationEngine(IReservationEngine inner, IAgentDemoPolicy policy) : IReservationEngine
{
    private void Guard(Guid ws, CancellationToken ct) { ct.ThrowIfCancellationRequested(); policy.DemandCommercialWrites(ws); }
    public Task<IEnumerable<TimeSlotDto>> GetAvailabilityAtAsync(Guid w, string l, string s, DateTime d, CancellationToken ct) => inner.GetAvailabilityAtAsync(w, l, s, d, ct);
    public Task<IEnumerable<TimeSlotDto>> GetAvailabilityAsync(Guid w, string l, string s, DateTime d, CancellationToken ct) => inner.GetAvailabilityAsync(w, l, s, d, ct);
    public Task<IEnumerable<TimeSlotDto>> GetAvailabilityForExistingAsync(Guid w, string p, Guid id, DateTime d, CancellationToken ct) => inner.GetAvailabilityForExistingAsync(w, p, id, d, ct);
    public Task<Result<ReservationDto>> ApplyExistingOperationAsync(ReservationOperationCommand operation, CancellationToken ct)
    { Guard(operation.WorkspaceId, ct); return inner.ApplyExistingOperationAsync(operation, ct); }
    public Task<Result<ReservationDto>> CreateReservationAsync(Guid w, string l, string s, string p, string n, DateTime d, CancellationToken ct, string? sourceMessageId = null)
    { Guard(w, ct); return inner.CreateReservationAsync(w, l, s, p, n, d, ct, sourceMessageId); }
    public Task<Result> CancelReservationAsync(Guid w, Guid id, CancellationToken ct) { Guard(w, ct); return inner.CancelReservationAsync(w, id, ct); }
    public Task<Result<Reservation>> CancelActiveReservationAsync(Guid w, string p, CancellationToken ct) { Guard(w, ct); return inner.CancelActiveReservationAsync(w, p, ct); }
    public Task<Result<ReservationDto>> EditReservationAsync(Guid w, Guid id, DateTime d, CancellationToken ct) { Guard(w, ct); return inner.EditReservationAsync(w, id, d, ct); }
    public Task<Result> CompleteReservationAsync(Guid w, Guid id, CancellationToken ct) { Guard(w, ct); return inner.CompleteReservationAsync(w, id, ct); }
}

public sealed class DemoOrderRepository(IOrderRepository inner, IAgentDemoPolicy policy) : IOrderRepository
{
    private void Guard(Guid w, CancellationToken ct) { ct.ThrowIfCancellationRequested(); policy.DemandCommercialWrites(w); }
    public Task<OrderRecord> CreateOrderAsync(Guid w, OrderRecord order, CancellationToken ct) { Guard(w, ct); return inner.CreateOrderAsync(w, order, ct); }
    public Task UpdateOrderStatusAsync(Guid w, string id, OrderStatus expected, OrderStatus next, CancellationToken ct) { Guard(w, ct); return inner.UpdateOrderStatusAsync(w, id, expected, next, ct); }
    public Task UpdateOrderAmountAsync(Guid w, string id, long amount, CancellationToken ct) { Guard(w, ct); return inner.UpdateOrderAmountAsync(w, id, amount, ct); }
    public Task<OrderRecord?> GetOrderByIdAsync(Guid w, string id, CancellationToken ct) => inner.GetOrderByIdAsync(w, id, ct);
    public Task<IEnumerable<OrderRecord>> GetOrdersAsync(Guid w, OrderStatus? status, CancellationToken ct, int limit = 50) => inner.GetOrdersAsync(w, status, ct, limit);
}
