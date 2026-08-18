using System.Text.RegularExpressions;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.ValueObjects;

public record Email
{
    public string Value { get; }

    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("El email no puede estar vacío.");

        if (!Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new DomainException("El formato del email es inválido.");

        Value = value.ToLowerInvariant();
    }

    public override string ToString() => Value;
}