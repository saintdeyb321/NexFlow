namespace NexFlow.Domain.Entities.System;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string ModuleCode { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? ActionUrl { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Notification() { } // Para EF Core

    public static Notification Create(Guid workspaceId, string moduleCode, string type, string title, string message, string? actionUrl)
    {
        return new Notification
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ModuleCode = moduleCode,
            Type = type,
            Title = title,
            Message = message,
            ActionUrl = actionUrl,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void MarkAsRead()
    {
        IsRead = true;
    }
}