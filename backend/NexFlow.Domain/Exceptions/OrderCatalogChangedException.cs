namespace NexFlow.Domain.Exceptions;

public enum OrderCatalogChange { PriceOrCurrency, Location, LocationDeleted, ProductUnavailable }

// A definite pre-commit rejection, never a CAS, idempotency, transport or human-control conflict.
public sealed class OrderCatalogChangedException(OrderCatalogChange change, string message) : ConcurrencyException(message)
{
    public OrderCatalogChange Change { get; } = change;
}
