using System;

namespace NexFlow.Domain.Entities;

public class Reservation
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string CustomerIdentifier { get; private set; } = null!;
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }
    public string Status { get; private set; } = null!;

    private Reservation() { }

    public static Reservation Create(Guid workspaceId, Guid locationId, Guid serviceId, string customerIdentifier, DateTime startTime, DateTime endTime)
    {
        return new Reservation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            LocationId = locationId,
            ServiceId = serviceId,
            CustomerIdentifier = customerIdentifier,
            StartTime = startTime,
            EndTime = endTime,
            Status = "CONFIRMED"
        };
    }
}