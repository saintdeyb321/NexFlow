using NexFlow.Application.Features.Services.DTOs;

namespace NexFlow.Application.Features.Reservations;

public enum ReservationResourceKind { Location, Service }
public sealed record ReservationResourceKey(Guid WorkspaceId, ReservationResourceKind Kind, string Id);
public sealed record ReservationResourceStamp(ReservationResourceKey Key, long Version, Guid? PendingOperationId);
public sealed record ReservationResourceChange(string Action, ServiceDto? Service = null, bool IsCreate = false,
    bool BlocksAllFuture = true, string[]? RetainedLocations = null, DateTime? ExpectedUpdatedAtUtc = null);
public sealed record ReservationResourceMutation(ReservationResourceKey Key, long Version, Guid OperationId, string PayloadJson);

public interface IReservationResourceCoordinator
{
    // Capture BEFORE reading Firestore; pending observations are usable only to retry the same mutation.
    Task<ReservationResourceStamp> ReadAsync(ReservationResourceKey key, CancellationToken ct);
    Task<ReservationResourceMutation> BeginAsync(ReservationResourceStamp observed, ReservationResourceChange change, CancellationToken ct);
    // Runs inside the reservation's SQL transaction; locks live until its commit/rollback.
    Task ValidateAsync(IReadOnlyList<ReservationResourceStamp> observed, CancellationToken ct);
    Task CompleteAsync(ReservationResourceMutation mutation, CancellationToken ct);
    Task<IReadOnlyList<ReservationResourceMutation>> ClaimPendingAsync(CancellationToken ct);
    // Recovery must obey the existing tenant-deletion lifecycle fence as API requests do.
    Task ExecuteRecoveryAsync(Guid workspaceId, Func<Task> apply, CancellationToken ct);
}
