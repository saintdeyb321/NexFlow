using NexFlow.Application.Abstractions;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Conversations;

public interface IHumanHandoffService
{
    Task EscalateToHumanAsync(Guid workspaceId, string conversationId, HandoffReason reason, CancellationToken ct);
}

public class HumanHandoffService : IHumanHandoffService
{
    private readonly IConversationRepository _conversationRepo;

    public HumanHandoffService(IConversationRepository conversationRepo)
    {
        _conversationRepo = conversationRepo;
    }

    public async Task EscalateToHumanAsync(Guid workspaceId, string conversationId, HandoffReason reason, CancellationToken ct)
    {
        await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversationId, ConversationMode.Human, reason, ct);
    }
}