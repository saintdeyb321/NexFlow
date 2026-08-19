namespace NexFlow.Application.Features.Business;

public record BusinessHoursDto(
    int DayOfWeek, // 0 = Domingo, 1 = Lunes...
    string OpenTime, // "08:00"
    string CloseTime, // "18:00"
    bool IsClosed
);