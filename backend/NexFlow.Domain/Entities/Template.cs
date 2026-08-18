using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class Template : Entity
{
    public string Name { get; private set; } = null!;
    public TemplateStatus Status { get; private set; }

    private Template() { }

    public static Template Create(string name)
    {
        return new Template { Name = name.ToUpperInvariant(), Status = TemplateStatus.Active };
    }
}