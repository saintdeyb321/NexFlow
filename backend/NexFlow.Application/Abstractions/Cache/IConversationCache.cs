namespace NexFlow.Application.Abstractions.Cache;

public class ConversationContextDto
{
    public string? StateVersion { get; set; }

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
    public string? TargetDate { get; set; }
    public string? TargetTime { get; set; }
    public string? RealCustomerName { get; set; }
    public string? OrderLastSourceMessageId { get; set; }
    public string? OrderLastResponse { get; set; }
    public List<OrderDraftItem> OrderDraftItems { get; set; } = new();


    public List<string> MissingFields { get; set; } = new();
    public string? LastQuestion { get; set; }
    public string? LastIntent { get; set; }
    public double? Confidence { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

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
