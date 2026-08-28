
namespace NexFlow.Application.Features.Requests;

public enum RequestStatus
{
    Pending,
    InReview,
    Approved,
    Rejected,
    Completed,
    Cancelled
}

public class RequestRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConsumerPhone { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}