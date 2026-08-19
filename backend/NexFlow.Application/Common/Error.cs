namespace NexFlow.Application.Common;

public sealed record Error(string Code, string Description)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "El valor no puede ser nulo.");
    public static readonly Error NotFound = new("Error.NotFound", "El recurso solicitado no fue encontrado.");
}