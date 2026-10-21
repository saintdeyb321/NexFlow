namespace NexFlow.Application.Features.Reservations;

public enum ReservationOperationKind
{
    None, CreateReservation, ViewExistingReservation, RescheduleExistingReservation,
    CancelExistingReservation, EditActiveDraft, AmbiguousReservationOperation
}

public sealed record ReservationCursor(DateTime StartTime, Guid Id);
public sealed record ReservationCandidate(Guid Id, string LocationId, string ServiceId, DateTime StartTime, DateTime EndTime, uint RowVersion);
public sealed record ReservationPage(IReadOnlyList<ReservationCandidate> Items, bool HasMore);

public static class ReservationContact
{
    public static void Validate(Guid workspaceId, string contact)
    {
        if (workspaceId == Guid.Empty || contact is not { Length: >= 11 and <= 16 } || contact[0] != '+'
            || !contact.Skip(1).All(char.IsAsciiDigit))
            throw new ArgumentException("An authorized workspace and canonical contact are required.");
    }
}
