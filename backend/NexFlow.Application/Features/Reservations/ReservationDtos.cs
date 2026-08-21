namespace NexFlow.Application.Features.Reservations;

public record TimeSlotDto(DateTime StartTime, DateTime EndTime, bool IsAvailable);

public record ReservationDto(
    Guid Id,
    Guid WorkspaceId,
    Guid LocationId,
    Guid ServiceId,
    string CustomerIdentifier, // Teléfono o WhatsApp
    DateTime StartTime,
    string Status // Ej: "CONFIRMED", "CANCELLED"
);