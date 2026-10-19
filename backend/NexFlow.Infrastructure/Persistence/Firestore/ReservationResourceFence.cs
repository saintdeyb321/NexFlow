using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Infrastructure.Persistence.Firestore;

// The terminal marker and the business write share ONE Firestore transaction.
// No SQL lock/transaction is kept while this class calls the remote store.
internal sealed class ReservationResourceFence(FirestoreDb db, IReservationResourceCoordinator resources)
{
    internal DocumentReference Reference(ReservationResourceMutation operation) => db.Collection("workspaces")
        .Document(operation.Key.WorkspaceId.ToString()).Collection("reservationResourceFences")
        .Document(operation.Key.Kind + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation.Key.Id))));

    public async Task<Result> ApplyAsync(ReservationResourceMutation operation, Func<Transaction, Task<Result>> write, CancellationToken ct)
    {
        ValidateIdentity(operation);
        Result result;
        try { result = await RunAsync(operation, write, ct); }
        catch (Exception ex) when (ex is DomainException or KeyNotFoundException or ConcurrencyException)
        {
            // Arbitrate a known validation rejection against any concurrent attempt.
            // If another attempt committed, its Applied marker wins. Unknown IO/ACK
            // failures are never rejected or unlocked here.
            result = await RunAsync(operation, _ => Task.FromResult(Result.Failure(new Error(
                ex is ConcurrencyException ? "Resource.Conflict" : "Resource.InvalidChange", ex.Message))), ct);
        }
        if (result.Error.Code == "Resource.Superseded")
            throw new ConcurrencyException(result.Error.Description);
        await resources.CompleteAsync(operation, ct);
        return result;
    }

    private Task<Result> RunAsync(ReservationResourceMutation operation, Func<Transaction, Task<Result>> write, CancellationToken ct) =>
        db.RunTransactionAsync(async tx =>
        {
            var reference = Reference(operation);
            var existing = await tx.GetSnapshotAsync(reference, ct);
            if (existing.Exists)
            {
                var version = existing.GetValue<long>("Version");
                if (version > operation.Version) return Result.Failure(new Error("Resource.Superseded", "Esta operación ya fue sustituida."));
                if (version == operation.Version)
                {
                    if (existing.GetValue<string>("OperationId") != operation.OperationId.ToString())
                        throw new InvalidOperationException("Resource fence identity mismatch.");
                    return existing.GetValue<bool>("Applied") ? Result.Success()
                        : Result.Failure(new Error(existing.GetValue<string>("ErrorCode"), existing.GetValue<string>("ErrorMessage")));
                }
            }
            var result = await write(tx);
            tx.Set(reference, new Dictionary<string, object>
            {
                ["Version"] = operation.Version, ["OperationId"] = operation.OperationId.ToString(),
                ["Applied"] = result.IsSuccess, ["ErrorCode"] = result.Error.Code,
                ["ErrorMessage"] = result.Error.Description
            });
            return result;
        }, cancellationToken: ct);

    internal static ReservationResourceChange Decode(ReservationResourceMutation operation)
    {
        ValidateIdentity(operation);
        try
        {
            var change = JsonSerializer.Deserialize<ReservationResourceChange>(operation.PayloadJson)
                ?? throw new InvalidReservationResourceMutationException();
            var valid = operation.Key.Kind == ReservationResourceKind.Location
                ? change.Action == "DeleteLocation" && change.Service == null
                : (change.Action == "DeactivateService" && change.Service == null)
                    || (change.Action == "SaveService" && change.Service?.Id == operation.Key.Id && change.Service.Type == "SERVICE");
            if (!valid) throw new InvalidReservationResourceMutationException();
            if (change.Service?.Metadata != null)
                change.Service.Metadata = change.Service.Metadata.ToDictionary(p => p.Key, p => Materialize(p.Value)!);
            return change;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { throw new InvalidReservationResourceMutationException(); }
    }

    private static void ValidateIdentity(ReservationResourceMutation operation)
    {
        if (operation.Version <= 0 || operation.OperationId == Guid.Empty || operation.Key.WorkspaceId == Guid.Empty
            || !Enum.IsDefined(operation.Key.Kind) || string.IsNullOrWhiteSpace(operation.Key.Id) || operation.Key.Id.Contains('/')
            || Encoding.UTF8.GetByteCount(operation.Key.Id) > 1500)
            throw new InvalidOperationException("Invalid resource mutation identity; administrative repair required.");
    }

    private static object? Materialize(object? value) => value is not JsonElement json ? value : json.ValueKind switch
    {
        JsonValueKind.Object => json.EnumerateObject().ToDictionary(p => p.Name, p => Materialize(p.Value)),
        JsonValueKind.Array => json.EnumerateArray().Select(v => Materialize(v)).ToList(),
        JsonValueKind.String => json.GetString(), JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Number => json.TryGetInt64(out var integer) ? (object)integer : json.GetDouble(),
        JsonValueKind.Null => null, _ => throw new InvalidOperationException("Invalid mutation metadata.")
    };
}

internal sealed class InvalidReservationResourceMutationException : Exception;
