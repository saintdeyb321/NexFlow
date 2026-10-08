using NexFlow.Domain.Entities.System;

namespace NexFlow.Application.Abstractions;

public sealed record WhatsAppConnectionSnapshot(string InstanceName, WhatsAppConnection Connection);
public sealed record WhatsAppObservation(string Status, bool HasSession, string? QrBase64 = null, DateTime? QrExpiresAt = null);

public interface IWhatsAppConnectionRepository
{
    Task<WhatsAppConnectionSnapshot> GetAsync(Guid workspaceId, CancellationToken ct);
    Task<Guid> AcquireAsync(Guid workspaceId, bool logout, DateTime now, DateTime until, CancellationToken ct);
    Task SaveObservationAsync(Guid workspaceId, Guid? operationId, WhatsAppObservation observation, DateTime now, CancellationToken ct, Guid? expectedVersion = null);
    Task ConfirmLogoutAsync(Guid workspaceId, Guid operationId, string? owner, DateTime now, CancellationToken ct);
    Task ReleaseAsync(Guid workspaceId, Guid operationId, CancellationToken ct);
    Task AdoptLegacyNameAsync(Guid workspaceId, string oldName, string providerName, CancellationToken ct);
}
