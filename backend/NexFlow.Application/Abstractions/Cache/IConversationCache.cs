namespace NexFlow.Application.Abstractions.Cache;

public class ConversationContextDto
{
    public string? StateVersion { get; set; }
    public List<ConversationMenuOption> RecoveryOptions { get; set; } = new();
    public string? RecoveryOptionsContext { get; set; }
    public int RecoveryOptionsOffset { get; set; }
    public bool RecoveryHasMore { get; set; }
    public int ClarificationAttempts { get; set; }
    public string? PendingRecoveryAction { get; set; }
    public string? RecoveryLastMessageId { get; set; }
    public string? RecoveryLastResponse { get; set; }
    public string? RecoveryLastInterpretation { get; set; }

    public string Mode { get; set; } = "Automatic"; // Automatic, Human
    public string? HandoffReason { get; set; }
    public DateTime? HandoffAt { get; set; }

    // 1. Estado de la Transacción
    public string? CurrentGoal { get; set; }
    public string? CurrentStep { get; set; }

    // 2. Entidades Recolectadas (Memoria)
    public string? SelectedServiceId { get; set; }
    public string? SelectedLocationId { get; set; }
    public int ReservationServiceOffset { get; set; }
    public string? ReservationCommitSourceMessageId { get; set; }
    public string? TargetDate { get; set; }
    public string? TargetTime { get; set; }
    public string? RealCustomerName { get; set; }
    // Additive fields inside the existing contextJson envelope, never another Firestore document/schema.
    public string? PendingCustomerName { get; set; }
    public List<string> PendingServiceIds { get; set; } = new();
    public List<string> PendingDialogueSlots { get; set; } = new();
    public NexFlow.Application.Features.Automation.Dialogue.Contracts.TimePreference? PreferredTime { get; set; }
    public string? OrderLastSourceMessageId { get; set; }
    public string? OrderLastResponse { get; set; }
    public List<OrderDraftItem> OrderDraftItems { get; set; } = new();


    public List<string> MissingFields { get; set; } = new();
    public string? LastQuestion { get; set; }
    public string? LastIntent { get; set; }
    public double? Confidence { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public sealed record ConversationMenuOption(string Key, string Label, string Kind);

public class OrderDraftItem
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}

public interface IConversationCache
{
    Task SetContextAsync(Guid workspaceId, string customerPhone, ConversationContextDto context, CancellationToken cancellationToken);
    Task<ConversationContextDto?> GetContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken);
    Task DeleteContextAsync(Guid workspaceId, string customerPhone, CancellationToken cancellationToken);
}
