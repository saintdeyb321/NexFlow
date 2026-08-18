using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class Module : Entity
{
    public string Code { get; private set; } = null!; // Ej: "FAQ", "RESERVATIONS"
    public string Name { get; private set; } = null!;
    public ModuleStatus Status { get; private set; }

    private Module() { }

    public static Module Create(string code, string name)
    {
        return new Module { Code = code.ToUpperInvariant(), Name = name, Status = ModuleStatus.Active };
    }
}