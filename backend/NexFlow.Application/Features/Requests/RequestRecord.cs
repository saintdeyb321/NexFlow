using System;

namespace NexFlow.Application.Features.Requests;

public class RequestRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConsumerPhone { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING"; // PENDING, IN_PROGRESS, COMPLETED, CANCELLED
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}