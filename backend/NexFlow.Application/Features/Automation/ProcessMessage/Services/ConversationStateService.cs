using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;
using System.Text.RegularExpressions;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IConversationStateService
{
    // 🔥 NUEVA FIRMA: Ahora devuelve un 'FastReply' si la regla determinista se cumple
    Task<(bool ShouldAiRespond, ConversationRecord Record, string? FastReply)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken);
}

public sealed class ConversationStateService : IConversationStateService
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IConsumerIdentityRepository _consumerRepo;
    private readonly IConversationCache _conversationCache;
    private readonly ILogger<ConversationStateService> _logger;

    public ConversationStateService(
        IConversationRepository conversationRepo,
        IConsumerIdentityRepository consumerRepo,
        IConversationCache conversationCache,
        ILogger<ConversationStateService> logger)
    {
        _conversationRepo = conversationRepo;
        _consumerRepo = consumerRepo;
        _conversationCache = conversationCache;
        _logger = logger;
    }

    public async Task<(bool ShouldAiRespond, ConversationRecord Record, string? FastReply)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);

        // 1. Mensajes Salientes (Enviados por Humanos desde NexFlow)
        if (request.FromMe)
        {
            if (conversation == null) return (false, null!, null);

            bool isAiMessage = await _conversationCache.IsMessageAiGeneratedAsync(workspaceId, request.MessageId, cancellationToken);
            if (isAiMessage) return (false, conversation, null);

            if (conversation.Mode != ConversationMode.Human)
            {
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, HandoffReason.ManualIntervention, cancellationToken);
                await _conversationCache.DeleteContextAsync(workspaceId, normalizedPhone, cancellationToken);
                conversation = conversation with { Mode = ConversationMode.Human, HandoffReason = HandoffReason.ManualIntervention };
            }

            await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Direction = "outbound", Sender = SenderType.BusinessUser, Content = request.MessageText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);
            return (false, conversation, null);
        }

        // 2. Registro del Consumidor e Inbound
        await _consumerRepo.UpsertConsumerAsync(workspaceId, new ConsumerIdentityRecord { Phone = normalizedPhone, DisplayName = request.CustomerName, FirstSeenAt = DateTime.UtcNow, LastInteractionAt = DateTime.UtcNow }, cancellationToken);

        conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Direction = "inbound", Sender = SenderType.Consumer, Content = request.MessageText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);

        if (conversation.Mode != ConversationMode.Automatic) return (false, conversation, null);

        // 3. Reglas Deterministas (Level 0 Fast Rules)
        var txt = request.MessageText.Trim().ToLowerInvariant();
        var wordCount = txt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        string? fastResponse = null;
        if (wordCount <= 3 && Regex.IsMatch(txt, @"^(hola|buenas|ola|buenos dias|buenas tardes|hey)$"))
            fastResponse = "¡Hola! Soy el asistente virtual. ¿En qué te puedo ayudar el día de hoy?";
        else if (wordCount <= 3 && Regex.IsMatch(txt, @"^(gracias|ok|perfecto|entendido|vale|listo)$"))
            fastResponse = "¡Con gusto! Si necesitas algo más, aquí estoy.";

        if (fastResponse != null)
        {
            _logger.LogInformation("Interceptado mensaje '{Msg}'. Resolviendo sin IA.", txt);
            return (false, conversation, fastResponse);
        }

        return (true, conversation, null);
    }
}