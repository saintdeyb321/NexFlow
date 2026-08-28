using System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.ValueObjects;

public record DateRange
{
    public DateTime Start { get; }
    // 🔥 SPRINT 1 (Auditoría #4): Permitir null para la licencia permanente
    public DateTime? End { get; }

    public DateRange(DateTime start, DateTime? end)
    {
        if (end.HasValue && start > end.Value)
            throw new DomainException("La fecha de inicio no puede ser mayor a la fecha de fin.");

        Start = start;
        End = end;
    }

    // 🔥 Si End es nulo, es permanente.
    public bool IsActive(DateTime date) => date >= Start && (!End.HasValue || date <= End.Value);

    public DateRange Extend(DateTime newEnd)
    {
        if (!End.HasValue)
            throw new DomainException("No se puede extender un rango permanente.");

        if (newEnd <= End.Value)
            throw new DomainException("La nueva fecha de fin debe ser mayor a la actual para extender.");

        return new DateRange(Start, newEnd);
    }

    public DateRange Renew(DateTime newStart, DateTime? newEnd)
    {
        return new DateRange(newStart, newEnd);
    }
}