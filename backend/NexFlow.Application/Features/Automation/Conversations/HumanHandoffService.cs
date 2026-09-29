using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Conversations;

public interface IHumanHandoffService
{
    Task EscalateToHumanAsync(Guid workspaceId, string conversationId, HandoffReason reason, CancellationToken ct);
    Task ReleaseToAutomaticAsync(Guid workspaceId, string conversationId, CancellationToken ct);
}

public class HumanHandoffService : IHumanHandoffService
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IContextRecoveryService _contextStore;

    public HumanHandoffService(IConversationRepository conversationRepo, IContextRecoveryService contextStore)
    {
        _conversationRepo = conversationRepo;
        _contextStore = contextStore;
    }

    public Task EscalateToHumanAsync(Guid workspaceId, string conversationId, HandoffReason reason, CancellationToken ct) =>
        SetModeAsync(workspaceId, conversationId, ConversationMode.Human, reason, ct);

    public Task ReleaseToAutomaticAsync(Guid workspaceId, string conversationId, CancellationToken ct) =>
        SetModeAsync(workspaceId, conversationId, ConversationMode.Automatic, HandoffReason.None, ct);

    private async Task SetModeAsync(Guid workspaceId, string conversationId, ConversationMode mode, HandoffReason reason, CancellationToken ct)
    {
        var conversation = await _conversationRepo.GetConversationAsync(workspaceId, conversationId, ct)
            ?? throw new InvalidOperationException("Conversation not found.");
        var context = await _contextStore.GetOrRecoverContextAsync(workspaceId, conversation.ConsumerPhone, ct);
        context.Mode = mode.ToString();
        context.HandoffReason = mode == ConversationMode.Human ? reason.ToString() : null;
        context.HandoffAt = mode == ConversationMode.Human ? DateTime.UtcNow : null;
        await _contextStore.SaveContextAsync(workspaceId, conversation.ConsumerPhone, context, ct);
        await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversationId, mode, reason, ct);
    }
}
