using System;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Conversations;

public record ConsumerIdentityRecord
{
    public string Phone { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public DateTime FirstSeenAt { get; init; }
    public DateTime LastInteractionAt { get; init; }
    public DateTime ExpiresAt { get; init; } // <-- NUEVO (TTL de 90 días)
}

public record ConversationRecord
{
    public string Id { get; init; } = string.Empty;
    public string ConsumerPhone { get; init; } = string.Empty;
    public string Channel { get; init; } = "whatsapp";
    public ConversationMode Mode { get; init; }
    public string Status { get; init; } = "open";
    public DateTime StartedAt { get; init; }
    public DateTime LastMessageAt { get; init; }
    public DateTime ExpiresAt { get; init; } // <-- NUEVO
}

public record MessageRecord
{
    public string Id { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public SenderType Sender { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? ExternalMessageId { get; init; }
    public DateTime Timestamp { get; init; }
    public DateTime ExpiresAt { get; init; } // <-- NUEVO
}