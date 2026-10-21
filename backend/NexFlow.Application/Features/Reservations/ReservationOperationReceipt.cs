using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Domain.Entities.System;

namespace NexFlow.Application.Features.Reservations;

public sealed record ReservationOperationCommand(Guid WorkspaceId, Guid ReservationId, string Contact, string SourceMessageId,
    ReservationOperationKind Action, uint ExpectedVersion, DateTime ExpectedStartTime, DateTime? ProposedUtc, string ConfirmationHash)
{
    public string Key => KeyFor(WorkspaceId, Contact, SourceMessageId);
    public string Fingerprint => Hash(JsonSerializer.Serialize(new { WorkspaceId, ReservationId, Contact, SourceMessageId, Action, ExpectedVersion, ExpectedStartTime, ProposedUtc, ConfirmationHash }));
    public static string KeyFor(Guid workspace, string contact, string source) => "resop_" + Hash(JsonSerializer.Serialize(new { workspace, contact, source }));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public void Validate()
    {
        ReservationContact.Validate(WorkspaceId, Contact);
        if (ReservationId == Guid.Empty || string.IsNullOrWhiteSpace(SourceMessageId) || SourceMessageId.Length > 256
            || ConfirmationHash.Length != 64 || ExpectedStartTime.Kind != DateTimeKind.Utc
            || Action is not (ReservationOperationKind.RescheduleExistingReservation or ReservationOperationKind.CancelExistingReservation)
            || Action == ReservationOperationKind.RescheduleExistingReservation && ProposedUtc?.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid reservation operation identity.");
    }
}

// Receipt and notification share the existing SQL row/PK and transaction. Never purge resop_ rows
// when cleaning delivery history: they remain business idempotency records until tenant deletion.
// Worker updates delivery columns only and deserializes the original N8n envelope, not Receipt.
public sealed record ReservationOperationReceipt(string Fingerprint, string ConfirmationHash, string ContactHash, ReservationDto Result)
{
    public static OutboxMessage Create(ReservationOperationCommand command, string eventType, object data, ReservationDto result, DateTime now)
    {
        var payload = new N8nEventPayload<object>(command.WorkspaceId, eventType, command.Key, command.Key, now, data);
        var json = JsonSerializer.SerializeToNode(payload)!.AsObject();
        json["ReservationOperationReceipt"] = JsonSerializer.SerializeToNode(new ReservationOperationReceipt(
            command.Fingerprint, command.ConfirmationHash, ReservationOperationCommand.Hash(command.Contact), result));
        return new() { Id = command.Key, WorkspaceId = command.WorkspaceId, EventType = eventType, PayloadJson = json.ToJsonString(), CreatedAt = now };
    }

    public static ReservationOperationReceipt Read(OutboxMessage message, Guid workspace, string contact)
    {
        if (message.WorkspaceId != workspace || !message.Id.StartsWith("resop_", StringComparison.Ordinal)) throw new InvalidOperationException("Operation scope mismatch.");
        var root = JsonNode.Parse(message.PayloadJson)!.AsObject();
        var receipt = root["ReservationOperationReceipt"]!.Deserialize<ReservationOperationReceipt>() ?? throw new InvalidOperationException("Missing durable receipt.");
        if (receipt.ContactHash != ReservationOperationCommand.Hash(contact) || receipt.Result.WorkspaceId != workspace
            || !ReservationContact.IsSameInternationalContact(receipt.Result.CustomerIdentifier, contact)) throw new InvalidOperationException("Operation owner mismatch.");
        return receipt;
    }
}
