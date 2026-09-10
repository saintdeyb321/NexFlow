using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IConversationStateService
{
    Task<(bool ShouldAiRespond, ConversationRecord Record)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken);
}

public class ConversationStateService : IConversationStateService
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IConsumerIdentityRepository _consumerRepo;
    private readonly IConversationCache _conversationCache;
    private readonly ILogger<ConversationStateService> _logger;

    public ConversationStateService(IConversationRepository conversationRepo, IConsumerIdentityRepository consumerRepo, IConversationCache conversationCache, ILogger<ConversationStateService> logger)
    {
        _conversationRepo = conversationRepo; _consumerRepo = consumerRepo; _conversationCache = conversationCache; _logger = logger;
    }

    public async Task<(bool ShouldAiRespond, ConversationRecord Record)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);

        if (request.FromMe)
        {
            if (conversation == null)
            {
                _logger.LogDebug("Ignoring outbound message {MessageId}.", request.MessageId);
                return (false, null!);
            }

            bool isAiMessage = await _conversationCache.IsMessageAiGeneratedAsync(workspaceId, request.MessageId, cancellationToken);
            if (isAiMessage) return (false, conversation);

            if (conversation.Mode != ConversationMode.Human)
            {
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, HandoffReason.ManualIntervention, cancellationToken);
                await _conversationCache.DeleteContextAsync(workspaceId, normalizedPhone, cancellationToken);
                conversation = conversation with { Mode = ConversationMode.Human, HandoffReason = HandoffReason.ManualIntervention };
            }

            await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Direction = "outbound", Sender = SenderType.BusinessUser, Content = request.MessageText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);
            return (false, conversation);
        }

        await _consumerRepo.UpsertConsumerAsync(workspaceId, new ConsumerIdentityRecord { Phone = normalizedPhone, DisplayName = request.CustomerName, FirstSeenAt = DateTime.UtcNow, LastInteractionAt = DateTime.UtcNow }, cancellationToken);
        conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);

        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Direction = "inbound", Sender = SenderType.Consumer, Content = request.MessageText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);

        bool shouldRespond = conversation.Mode == ConversationMode.Automatic;
        return (shouldRespond, conversation);
    }
}