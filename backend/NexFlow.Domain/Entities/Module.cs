using System;
using System.Collections.Generic;
using System.Linq;
using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class Module : Entity
{
    public string Code { get; private set; } = null!; // Ej: "FAQ", "RESERVATIONS"
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public ModuleStatus Status { get; private set; }

    // NUEVO: El módulo ahora es dueño de sus capacidades
    private readonly List<ModuleCapability> _capabilities = new();
    public IReadOnlyCollection<ModuleCapability> Capabilities => _capabilities.AsReadOnly();

    private Module() { }

    public static Module Create(string code, string name, string? description = null)
    {
        return new Module
        {
            Id = Guid.NewGuid(),
            Code = code.ToUpperInvariant(),
            Name = name,
            Description = description,
            Status = ModuleStatus.Active
        };
    }

    // NUEVO: Método para inyectar capacidades en el Seeder
    public void AddCapability(string code, string description)
    {
        var upperCode = code.ToUpperInvariant();
        if (!_capabilities.Any(c => c.Code == upperCode))
        {
            _capabilities.Add(new ModuleCapability(this.Id, upperCode, description));
        }
    }
}