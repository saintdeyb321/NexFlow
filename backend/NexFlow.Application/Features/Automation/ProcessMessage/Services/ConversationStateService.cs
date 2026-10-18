using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
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
    private readonly IContextRecoveryService _contextStore;
    private readonly IHumanHandoffService _handoffService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ConversationStateService> _logger;

    private readonly TimeSpan _sessionTimeout = TimeSpan.FromHours(24);

    public ConversationStateService(
        IConversationRepository conversationRepo,
        IConsumerIdentityRepository consumerRepo,
        IContextRecoveryService contextStore,
        IHumanHandoffService handoffService,
        INotificationService notificationService,
        ILogger<ConversationStateService> logger)
    {
        _conversationRepo = conversationRepo;
        _consumerRepo = consumerRepo;
        _contextStore = contextStore;
        _handoffService = handoffService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<(bool ShouldAiRespond, ConversationRecord Record, string? FastReply)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // Keep the full input in the SQL inbound envelope; bound only its conversation-history projection.
        var historyText = request.MessageText.Length <= 8000 ? request.MessageText : request.MessageText[..8000] + " [Mensaje extenso conservado en la recepción durable]";
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);

        var sessionContext = await _contextStore.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);

        if (!request.FromMe && string.IsNullOrWhiteSpace(sessionContext.CurrentGoal) && conversation != null && conversation.Mode == ConversationMode.Automatic && (DateTime.UtcNow - conversation.LastMessageAt) > _sessionTimeout)
        {
            _logger.LogInformation("La conversación {ConvId} ha expirado por inactividad. Cerrando sesión.", conversation.Id);
            await _conversationRepo.CloseConversationAsync(workspaceId, conversation.Id, cancellationToken);
            await _contextStore.DeleteContextAsync(workspaceId, normalizedPhone, cancellationToken);
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
                var reconciliation = await _conversationRepo.ReconcileOutboundEchoAsync(workspaceId, conversation?.Id,
                    normalizedPhone, request.MessageText, request.MessageId, request.ObservedAtUtc ?? DateTime.UtcNow, cancellationToken);
                origin = reconciliation.Origin;
                if (reconciliation.Ambiguous)
                {
                    _logger.LogError("FromMe {ExternalId} cannot be uniquely reconciled in workspace {WorkspaceId}; no origin or takeover was assigned.",
                        request.MessageId, workspaceId);
                    await _notificationService.NotifyAsync(workspaceId, "CONVERSATIONS", NotificationType.SystemAlert,
                        "Origen de envío sin confirmar",
                        $"El eco {request.MessageId} tiene posibles envíos de NexFlow sin confirmar. Revisión requerida; no se asumió control humano.",
                        "/inbox", cancellationToken);
                    return (false, conversation!, null);
                }
                if (origin == null && await _conversationRepo.HasUnconfirmedOutboundAsync(workspaceId, normalizedPhone, cancellationToken))
                {
                    // A transport may have started while reading the first query.
                    reconciliation = await _conversationRepo.ReconcileOutboundEchoAsync(workspaceId, conversation?.Id,
                        normalizedPhone, request.MessageText, request.MessageId, request.ObservedAtUtc ?? DateTime.UtcNow, cancellationToken);
                    origin = reconciliation.Origin;
                    if (reconciliation.Ambiguous)
                    {
                        _logger.LogError("FromMe {ExternalId} remains ambiguous in workspace {WorkspaceId}.", request.MessageId, workspaceId);
                        return (false, conversation!, null);
                    }
                }
                origin ??= await _conversationRepo.GetMessageOriginAsync(workspaceId, normalizedPhone, request.MessageId, cancellationToken);
            }
            if (origin == MessageOrigin.NexFlowAI) return (false, conversation!, null);

            conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
            await _handoffService.EscalateToHumanAsync(workspaceId, conversation.Id, HandoffReason.ManualIntervention, cancellationToken);
            conversation = conversation with { Mode = ConversationMode.Human, HandoffReason = HandoffReason.ManualIntervention };
            // Registramos el mensaje como enviado por el humano
            if (origin == null)
                await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Origin = MessageOrigin.WhatsAppHuman, Direction = "outbound", Sender = SenderType.BusinessUser, Content = historyText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);
            return (false, conversation, null);
        }

        // Si es un mensaje del Cliente:
        await _consumerRepo.UpsertConsumerAsync(workspaceId, new ConsumerIdentityRecord { Phone = normalizedPhone, DisplayName = request.CustomerName, FirstSeenAt = DateTime.UtcNow, LastInteractionAt = DateTime.UtcNow }, cancellationToken);

        conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Origin = MessageOrigin.Consumer, Direction = "inbound", Sender = SenderType.Consumer, Content = historyText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);

        var stateLock = GetStateLock(workspaceId, conversation.Id);
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            var latestConversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
            if (latestConversation == null || latestConversation.Id != conversation.Id)
                return (false, conversation, null);
            conversation = latestConversation;

            var context = await _contextStore.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);
            if (conversation.Mode != ConversationMode.Automatic || context.Mode == "Human")
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

            // Existing queued envelopes can still contain the former media markers without Kind.
            if (request.Kind != IncomingMessageKind.Text || request.MessageText is "[El cliente envió una imagen]"
                or "[El cliente envió un audio]" or "[El cliente envió un documento]")
                return (false, conversation, "No puedo interpretar imágenes, audios, documentos, videos ni stickers. Escribe tu consulta en un mensaje de texto para continuar.");

            if (!string.IsNullOrWhiteSpace(context.CurrentGoal)) return (true, conversation, null);


            return (true, conversation, null);
        }
        finally
        {
            stateLock.Release();
        }
    }
}
