using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Conversations;

public interface IHumanHandoffService
{
    Task EscalateToHumanAsync(Guid workspaceId, string conversationId, HandoffReason reason, CancellationToken ct);
}

public class HumanHandoffService : IHumanHandoffService
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IConversationCache _conversationCache;

    public HumanHandoffService(IConversationRepository conversationRepo, IConversationCache conversationCache)
    {
        _conversationRepo = conversationRepo;
        _conversationCache = conversationCache;
    }

    public async Task EscalateToHumanAsync(Guid workspaceId, string conversationId, HandoffReason reason, CancellationToken ct)
    {
        await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversationId, ConversationMode.Human, reason, ct);

        var conversation = await _conversationRepo.GetConversationAsync(workspaceId, conversationId, ct);
        if (conversation != null)
        {
            var context = await _conversationCache.GetContextAsync(workspaceId, conversation.ConsumerPhone, ct) ?? new ConversationContextDto();
            context.Mode = "Human";
            context.HandoffReason = reason.ToString();
            context.HandoffAt = DateTime.UtcNow;
            await _conversationCache.SetContextAsync(workspaceId, conversation.ConsumerPhone, context, ct);
        }
    }
}