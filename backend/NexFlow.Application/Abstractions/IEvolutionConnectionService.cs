using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Abstractions.Integrations;

public interface IEvolutionConnectionService
{
    Task<WhatsAppConnectionStatus> GetStatusAsync(Guid workspaceId, bool refresh, CancellationToken ct);
    Task<WhatsAppConnectionStatus> ConnectAsync(Guid workspaceId, CancellationToken ct);
    Task<WhatsAppConnectionStatus> DisconnectAsync(Guid workspaceId, bool confirmed, CancellationToken ct);
    Task ObserveConnectionAsync(Guid workspaceId, string state, CancellationToken ct);
}

public sealed record WhatsAppConnectionStatus(string Status, bool IsLinked, bool CanConnect, bool RequiresLogout,
    string? QrBase64 = null, DateTime? QrExpiresAt = null, string? Message = null);
