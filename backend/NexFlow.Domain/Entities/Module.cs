using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class Module : Entity
{
    public string Code { get; private set; } = null!; // Ej: "FAQ", "RESERVATIONS"
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public ModuleStatus Status { get; private set; }
    private Module() { }
    public static Module Create(string code, string name, string? description = null)
    {
        return new Module
        {
            Code = code.ToUpperInvariant(),
            Name = name,
            Description = description,
            Status = ModuleStatus.Active
        };
    }
}