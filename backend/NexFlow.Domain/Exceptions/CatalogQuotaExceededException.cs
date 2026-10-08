namespace NexFlow.Domain.Exceptions;

public sealed class CatalogQuotaExceededException(string message) : DomainException(message);
