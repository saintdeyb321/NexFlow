using System;

namespace NexFlow.Domain.Entities;

public class ModuleCapability : Entity
{
    public Guid ModuleId { get; private set; }
    public string Code { get; private set; } = null!; // Ej: "CHECK_AVAILABILITY"
    public string Description { get; private set; } = null!;

    private ModuleCapability() { }

    public ModuleCapability(Guid moduleId, string code, string description)
    {
        Id = Guid.NewGuid();
        ModuleId = moduleId;
        Code = code.ToUpperInvariant();
        Description = description;
    }
}