namespace NexFlow.Application.Features.Reservations;

public record TimeSlotDto(DateTime StartTime, DateTime EndTime, bool IsAvailable);

public record ReservationDto(
    Guid Id,
    Guid WorkspaceId,
    string LocationId,
    string ServiceId,
    string CustomerName,
    string CustomerIdentifier,
    DateTime DateTime, // Cambiado de StartTime a DateTime para coincidir con frontend
    string Status
);