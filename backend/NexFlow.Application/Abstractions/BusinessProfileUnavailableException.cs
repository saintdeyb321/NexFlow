namespace NexFlow.Application.Abstractions;

// The persistence adapter classifies transient failures without leaking gRPC into application code.
public sealed class BusinessProfileUnavailableException(bool timedOut, Exception innerException)
    : Exception("Business profile is temporarily unavailable.", innerException)
{
    public bool TimedOut { get; } = timedOut;
}
