namespace NexFlow.Domain.Entities.System;

// The provider may be offline while the WhatsApp session remains linked.
public sealed class WhatsAppConnection
{
    public Guid WorkspaceId { get; set; }
    public Guid PersistenceVersion { get; set; }
    public bool? IsLinked { get; set; }
    public string Status { get; set; } = "DISCONNECTED";
    public string? QrBase64 { get; set; }
    public DateTime? QrExpiresAt { get; set; }
    public DateTime? ObservedAt { get; set; }
    public Guid? OperationId { get; set; }
    public DateTime? OperationUntil { get; set; }
    public bool LogoutPending { get; set; }
    public DateTime? LastLogoutAt { get; set; }
    public string? LoggedOutOwner { get; set; }
}
