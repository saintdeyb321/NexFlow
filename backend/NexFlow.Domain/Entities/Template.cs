using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class Template : Entity
{
    public string Code { get; private set; } = null!; // Ej: "SECRETARY", "OPERATIONS"
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public TemplateStatus Status { get; private set; }

    private Template() { }

    public static Template Create(string code, string name, string? description = null)
    {
        return new Template
        {
            Code = code.ToUpperInvariant(),
            Name = name,
            Description = description,
            Status = TemplateStatus.Active
        };
    }
}