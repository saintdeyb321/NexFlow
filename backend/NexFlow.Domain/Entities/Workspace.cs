using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class Workspace : Entity
{
    public string Name { get; private set; } = null!;
    public WorkspaceStatus Status { get; private set; }

    // 🔥 Identificador único de la conexión en Evolution API (ej. "mi-negocio-01")
    public string? EvolutionInstanceName { get; private set; }

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
    public void Suspend() { EnsureNotDeleting(); Status = WorkspaceStatus.Suspended; }
    public void Activate() { EnsureNotDeleting(); Status = WorkspaceStatus.Active; }
    public void Archive() { EnsureNotDeleting(); Status = WorkspaceStatus.Archived; }
    private void EnsureNotDeleting()
    {
        if (Status == WorkspaceStatus.Deleting) throw new NexFlow.Domain.Exceptions.DomainException("El workspace está en eliminación.");
    }
    public void BeginDeletion() => Status = WorkspaceStatus.Deleting;
    public void LinkEvolutionInstance(string instanceName) => EvolutionInstanceName = instanceName;
}
