namespace NexFlow.Application.Features.Automation.ProcessMessage;

public enum IncomingMessageKind { Text, Image, Audio, Document, Sticker, Video, Unsupported, Control }

public record ProcessIncomingMessageCommand(
    string InstanceName,
    string CustomerPhone,
    string CustomerName,
    string MessageText,
    string MessageId,
    bool FromMe,
    Guid? WorkspaceId = null,
    DateTime? ObservedAtUtc = null,
    IncomingMessageKind Kind = IncomingMessageKind.Text
);
