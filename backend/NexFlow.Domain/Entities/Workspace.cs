using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class Workspace : Entity
{
    public string Name { get; private set; } = null!;
    public WorkspaceStatus Status { get; private set; }

    private Workspace() { }

    public static Workspace Create(string name)
    {
        return new Workspace
        {
            Name = name,
            Status = WorkspaceStatus.Active
        };
    }

    public void Rename(string newName) => Name = newName;

    public void Suspend() => Status = WorkspaceStatus.Suspended;

    public void Activate() => Status = WorkspaceStatus.Active;

    public void Archive() => Status = WorkspaceStatus.Archived;
}