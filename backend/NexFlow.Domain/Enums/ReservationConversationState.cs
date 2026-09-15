namespace NexFlow.Domain.Enums;

public enum ReservationConversationState
{
    Idle,
    SelectingLocation,
    SelectingService,
    SelectingDate,
    SelectingTime,
    Confirming,
    Completed,
    Cancelled,
    Failed
}