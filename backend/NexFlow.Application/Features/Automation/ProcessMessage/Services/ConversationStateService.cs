using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;
using System.Text.RegularExpressions;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IConversationStateService
{
    Task<(bool ShouldAiRespond, ConversationRecord Record, string? FastReply)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken);
}

public sealed class ConversationStateService : IConversationStateService
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IConsumerIdentityRepository _consumerRepo;
    private readonly IConversationCache _conversationCache;
    private readonly ILogger<ConversationStateService> _logger;

    // 🔥 SPRINT 08: Tiempo máximo de inactividad antes de considerar una nueva sesión (24 horas)
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromHours(24);

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

        // 🔥 SPRINT 08: Control de Sesión. Si la conversación existe pero es muy vieja, la cerramos.
        if (conversation != null && (DateTime.UtcNow - conversation.LastMessageAt) > _sessionTimeout)
        {
            _logger.LogInformation("La conversación {ConvId} ha expirado por inactividad. Cerrando sesión.", conversation.Id);
            await _conversationRepo.CloseConversationAsync(workspaceId, conversation.Id, cancellationToken);
            await _conversationCache.DeleteContextAsync(workspaceId, normalizedPhone, cancellationToken);
            conversation = null; // Forzamos la creación de una nueva sesión abajo
        }

        // 1. Mensajes Salientes (Enviados por Humanos desde NexFlow)
        if (request.FromMe)
        {
            if (conversation == null) return (false, null!, null);

            bool isAiMessage = await _conversationCache.IsMessageAiGeneratedAsync(workspaceId, request.MessageId, cancellationToken);

            if (!isAiMessage)
            {
                var dbMessage = await _conversationRepo.GetMessageByExternalIdAsync(workspaceId, request.MessageId, cancellationToken);
                if (dbMessage != null && dbMessage.Sender == SenderType.AI)
                {
                    isAiMessage = true;
                }
            }

            if (isAiMessage) return (false, conversation, null);

            if (conversation.Mode != ConversationMode.Human)
            {
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, HandoffReason.ManualIntervention, cancellationToken);

                var context = await _conversationCache.GetContextAsync(workspaceId, normalizedPhone, cancellationToken) ?? new ConversationContextDto();
                context.Mode = "Human";
                context.HandoffReason = HandoffReason.ManualIntervention.ToString();
                context.HandoffAt = DateTime.UtcNow;
                await _conversationCache.SetContextAsync(workspaceId, normalizedPhone, context, cancellationToken);

                conversation = conversation with { Mode = ConversationMode.Human, HandoffReason = HandoffReason.ManualIntervention };
            }

            await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Direction = "outbound", Sender = SenderType.BusinessUser, Content = request.MessageText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);
            return (false, conversation, null);
        }

        // 2. Registro del Consumidor e Inbound
        await _consumerRepo.UpsertConsumerAsync(workspaceId, new ConsumerIdentityRecord { Phone = normalizedPhone, DisplayName = request.CustomerName, FirstSeenAt = DateTime.UtcNow, LastInteractionAt = DateTime.UtcNow }, cancellationToken);

        // Si era nula (o fue cerrada por vieja), aquí se crea la nueva sesión fresca
        conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Direction = "inbound", Sender = SenderType.Consumer, Content = request.MessageText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);

        if (conversation.Mode != ConversationMode.Automatic) return (false, conversation, null);

        // 3. Reglas Deterministas (Level 0 Fast Rules)
        var txt = request.MessageText.Trim().ToLowerInvariant();
        var wordCount = txt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        string? fastResponse = null;
        if (wordCount <= 3 && Regex.IsMatch(txt, @"^(hola|buenas|ola|buenos dias|buenas tardes|hey)$"))
        {
            await _conversationCache.DeleteContextAsync(workspaceId, normalizedPhone, cancellationToken);
            fastResponse = "¡Hola! Soy el asistente virtual. ¿En qué te puedo ayudar el día de hoy?";
        }
        else if (wordCount <= 3 && Regex.IsMatch(txt, @"^(gracias|ok|perfecto|entendido|vale|listo)$"))
        {
            fastResponse = "¡Con gusto! Si necesitas algo más, aquí estoy.";
        }

        if (fastResponse != null)
        {
            _logger.LogInformation("Interceptado mensaje '{Msg}'. Resolviendo sin IA.", txt);
            return (false, conversation, fastResponse);
        }

        return (true, conversation, null);
    }
}