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

    // Método de utilidad para extender la licencia
    public DateRange Extend(DateTime newEnd) => new DateRange(Start, newEnd);
}