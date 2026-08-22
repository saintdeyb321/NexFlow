namespace NexFlow.Domain.Enums;

public enum WorkspaceStatus
{
    Pending = 0,   // Nuevo estado inicial
    Active = 1,
    Suspended = 2,
    Cancelled = 3,
    Archived = 4
}