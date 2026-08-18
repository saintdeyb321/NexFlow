using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class AuditLog : Entity
{
    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public AuditAction Action { get; private set; }
    public string Details { get; private set; } = null!; // Formato JSON ligero si es necesario

    private AuditLog() { }

    public static AuditLog Create(Guid workspaceId, Guid userId, AuditAction action, string details)
    {
        return new AuditLog
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Action = action,
            Details = details
        };
    }
}