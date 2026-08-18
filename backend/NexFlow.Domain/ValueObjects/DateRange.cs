using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.ValueObjects;

public record DateRange
{
    public DateTime Start { get; }
    public DateTime End { get; }

    public DateRange(DateTime start, DateTime end)
    {
        if (start > end)
            throw new DomainException("La fecha de inicio no puede ser mayor a la fecha de fin.");

        Start = start;
        End = end;
    }

    public bool IsActive(DateTime date) => date >= Start && date <= End;

    public DateRange Extend(DateTime newEnd)
    {
        if (newEnd <= End)
            throw new DomainException("La nueva fecha de fin debe ser mayor a la actual para extender.");

        return new DateRange(Start, newEnd);
    }

    public DateRange Renew(DateTime newStart, DateTime newEnd)
    {
        return new DateRange(newStart, newEnd);
    }
}