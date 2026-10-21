using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Abstractions.Cache;

// At most one page and one candidate, in the existing CAS-protected contextJson. No reservation archive.
public sealed class ReservationManagementContext
{
    public Guid WorkspaceId { get; set; }
    public string ContactHash { get; set; } = "";
    public ReservationOperationKind Operation { get; set; }
    public string Stage { get; set; } = "Select";
    public List<ReservationCandidate> Options { get; set; } = [];
    public bool HasMore { get; set; }
    public ReservationCursor? Cursor { get; set; }
    public ReservationCandidate? Selected { get; set; }
    public string? ProposedDate { get; set; }
    public string? ProposedTime { get; set; }
    public string ZoneId { get; set; } = "";
    public List<TimeSlotDto> Slots { get; set; } = [];
    public DateTime ExpiresAt { get; set; }
    public string NextQuestion { get; set; } = "";
    public ReservationOperationCommand? PendingCommand { get; set; }
    public bool SuspendedDraftExpired { get; set; }
    public string? SuspendedGoal { get; set; }
    public string? SuspendedStep { get; set; }
}
