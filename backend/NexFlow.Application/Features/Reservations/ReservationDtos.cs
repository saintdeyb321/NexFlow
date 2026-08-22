namespace NexFlow.Application.Features.Reservations;

public record TimeSlotDto(DateTime StartTime, DateTime EndTime, bool IsAvailable);

public record ReservationDto(
    Guid Id,
    Guid WorkspaceId,
    string LocationId,
    string ServiceId,
    string CustomerIdentifier,
    string CustomerName, // <--- Moverlo aquí para que coincida con el Engine
    DateTime StartTime,
    string Status
);