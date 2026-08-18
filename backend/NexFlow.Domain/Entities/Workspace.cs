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

    public void Rename(string newName)
    {
        Name = newName;
        UpdateTimestamp();
    }

    public void Suspend()
    {
        Status = WorkspaceStatus.Suspended;
        UpdateTimestamp();
    }

    public void Activate()
    {
        Status = WorkspaceStatus.Active;
        UpdateTimestamp();
    }

    public void Archive()
    {
        Status = WorkspaceStatus.Archived;
        UpdateTimestamp();
    }
}