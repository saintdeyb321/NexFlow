namespace NexFlow.Application.Abstractions;

// Nos dice en qué negocio está operando el usuario en esta petición HTTP
public interface IWorkspaceContext
{
    Guid CurrentWorkspaceId { get; }
    bool HasWorkspace { get; }
}