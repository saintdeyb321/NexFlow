namespace NexFlow.Application.Abstractions;

// Extraerá el WorkspaceId del header HTTP (ej. x-workspace-id) para asegurar el tenant
public interface IWorkspaceContext
{
    Guid CurrentWorkspaceId { get; }
    bool HasWorkspace { get; }
}