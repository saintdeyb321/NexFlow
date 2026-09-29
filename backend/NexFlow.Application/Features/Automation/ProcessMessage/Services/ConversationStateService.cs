using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Enums;
using System.Text.RegularExpressions;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IConversationStateService
{
    // 🔥 SPRINT 03: Eliminamos la necesidad de pasar request a ProcessStateAsync para evitar errores de firma.
    // Pasamos solo los datos que realmente necesita el método.
    Task<(bool ShouldAiRespond, ConversationRecord Record, string? FastReply)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken);
}

public sealed class ConversationStateService : IConversationStateService
{
    private static readonly SemaphoreSlim[] StateLocks = Enumerable.Range(0, 1024)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public static SemaphoreSlim GetStateLock(Guid workspaceId, string conversationId)
    {
        var hash = HashCode.Combine(workspaceId, conversationId);
        return StateLocks[(int)((uint)hash % (uint)StateLocks.Length)];
    }

    private readonly IConversationRepository _conversationRepo;
    private readonly IConsumerIdentityRepository _consumerRepo;
    private readonly IConversationCache _conversationCache;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ConversationStateService> _logger;

    private readonly TimeSpan _sessionTimeout = TimeSpan.FromHours(24);

    public ConversationStateService(
        IConversationRepository conversationRepo,
        IConsumerIdentityRepository consumerRepo,
        IConversationCache conversationCache,
        INotificationService notificationService,
        ILogger<ConversationStateService> logger)
    {
        _conversationRepo = conversationRepo;
        _consumerRepo = consumerRepo;
        _conversationCache = conversationCache;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<(bool ShouldAiRespond, ConversationRecord Record, string? FastReply)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);

        if (!request.FromMe && conversation != null && conversation.Mode == ConversationMode.Automatic && (DateTime.UtcNow - conversation.LastMessageAt) > _sessionTimeout)
        {
            _logger.LogInformation("La conversación {ConvId} ha expirado por inactividad. Cerrando sesión.", conversation.Id);
            await _conversationRepo.CloseConversationAsync(workspaceId, conversation.Id, cancellationToken);
            await _conversationCache.DeleteContextAsync(workspaceId, normalizedPhone, cancellationToken);
            conversation = null;
        }

        // 🔥 SPRINT 03: Manejo Estricto de Mensajes Salientes (FromMe)
        if (request.FromMe)
        {
            var origin = await _conversationRepo.GetMessageOriginAsync(workspaceId, normalizedPhone, request.MessageId, cancellationToken);
            if (origin == null && conversation != null)
            {
                // Compatibility with messages persisted before the origin ledger.
                var existing = await _conversationRepo.GetMessageByExternalIdAsync(workspaceId, conversation.Id, request.MessageId, cancellationToken);
                if (existing?.Direction == "outbound")
                    origin = existing.Origin ?? (existing.Sender == SenderType.AI ? MessageOrigin.NexFlowAI : MessageOrigin.NexFlowHuman);
            }
            if (origin == null)
            {
                if (await _conversationRepo.HasUnconfirmedOutboundAsync(workspaceId, normalizedPhone, cancellationToken))
                    throw new InvalidOperationException("FromMe origin cannot be determined while an outbound attempt is unconfirmed.");
                // Confirmation may have committed between the first origin lookup
                // and the pending lookup. Re-read before classifying a human.
                origin = await _conversationRepo.GetMessageOriginAsync(workspaceId, normalizedPhone, request.MessageId, cancellationToken);
            }
            if (origin == MessageOrigin.NexFlowAI) return (false, conversation!, null);

            conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
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

            // Registramos el mensaje como enviado por el humano
            if (origin == null)
                await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Origin = MessageOrigin.WhatsAppHuman, Direction = "outbound", Sender = SenderType.BusinessUser, Content = request.MessageText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);
            return (false, conversation, null);
        }

        // Si es un mensaje del Cliente:
        await _consumerRepo.UpsertConsumerAsync(workspaceId, new ConsumerIdentityRecord { Phone = normalizedPhone, DisplayName = request.CustomerName, FirstSeenAt = DateTime.UtcNow, LastInteractionAt = DateTime.UtcNow }, cancellationToken);

        conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Origin = MessageOrigin.Consumer, Direction = "inbound", Sender = SenderType.Consumer, Content = request.MessageText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);

        var stateLock = GetStateLock(workspaceId, conversation.Id);
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            var latestConversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
            if (latestConversation == null || latestConversation.Id != conversation.Id)
                return (false, conversation, null);
            conversation = latestConversation;

            if (conversation.Mode != ConversationMode.Automatic)
            {
                await _notificationService.NotifyAsync(
                    workspaceId,
                    "CONVERSATIONS",
                    NotificationType.NewMessage,
                    "Nuevo mensaje en chat manual",
                    $"El cliente {request.CustomerName ?? normalizedPhone} ha respondido.",
                    $"/inbox?conversation={conversation.Id}",
                    cancellationToken);

                return (false, conversation, null);
            }

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
        finally
        {
            stateLock.Release();
        }
    }
}