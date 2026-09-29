namespace NexFlow.Domain.Enums;

public static class OrderLifecycle
{
    public static bool CanTransition(OrderStatus current, OrderStatus next) => current switch
    {
        OrderStatus.PendingReview => next is OrderStatus.Approved or OrderStatus.Rejected or OrderStatus.Cancelled,
        OrderStatus.Approved => next is OrderStatus.Processing or OrderStatus.Cancelled,
        OrderStatus.Processing => next is OrderStatus.Completed or OrderStatus.Cancelled,
        _ => false
    };
}
