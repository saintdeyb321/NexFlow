namespace NexFlow.Application.Features.Notifications;

public class NotificationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ModuleCode { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;
    public string? ActionUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}