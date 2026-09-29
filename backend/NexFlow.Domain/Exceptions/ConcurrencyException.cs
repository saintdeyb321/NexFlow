namespace NexFlow.Domain.Exceptions;

public sealed class ConcurrencyException(string message) : Exception(message) { }
