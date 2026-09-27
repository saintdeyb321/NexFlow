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

        // 🔥 SPRINT 08: Control de Sesión. Si la conversación es muy vieja, la cerramos.
        // Solo la IA (Automatic) olvida la sesión antigua. Si está en 'Human', el agente asume la responsabilidad.
        if (conversation != null && conversation.Mode == ConversationMode.Automatic && (DateTime.UtcNow - conversation.LastMessageAt) > _sessionTimeout)
        {
            _logger.LogInformation("La conversación {ConvId} ha expirado por inactividad. Cerrando sesión.", conversation.Id);
            await _conversationRepo.CloseConversationAsync(workspaceId, conversation.Id, cancellationToken);
            await _conversationCache.DeleteContextAsync(workspaceId, normalizedPhone, cancellationToken);
            conversation = null;
        }

        // 1. Mensajes Salientes (Enviados por Humanos desde la Bandeja de Entrada NexFlow)
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

            // Si fue la IA quien envió el mensaje (a través del Outbox), ignoramos.
            if (isAiMessage) return (false, conversation, null);

            // 🔥 SPRINT 07: HUMAN TAKEOVER IMPLÍCITO (Sin borrar contexto)
            // Si el humano escribe desde NexFlow, la IA se pone en pausa, PERO el contexto sobrevive[cite: 1].
            if (conversation.Mode != ConversationMode.Human)
            {
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, HandoffReason.ManualIntervention, cancellationToken);

                // Recuperamos el contexto de Redis, actualizamos el estado, y lo VOLVEMOS a guardar.
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

        // 2. Registro del Consumidor e Inbound (Mensaje entrante de WhatsApp)
        await _consumerRepo.UpsertConsumerAsync(workspaceId, new ConsumerIdentityRecord { Phone = normalizedPhone, DisplayName = request.CustomerName, FirstSeenAt = DateTime.UtcNow, LastInteractionAt = DateTime.UtcNow }, cancellationToken);

        conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord { Id = request.MessageId, Direction = "inbound", Sender = SenderType.Consumer, Content = request.MessageText, ExternalMessageId = request.MessageId, Status = MessageStatus.Sent, Timestamp = DateTime.UtcNow }, cancellationToken);

        var stateLock = GetStateLock(workspaceId, conversation.Id);
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            var latestConversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
            if (latestConversation == null || latestConversation.Id != conversation.Id)
                return (false, conversation, null);
            conversation = latestConversation;

            // 🔥 Si la IA está apagada, no responde, pero el mensaje ya quedó registrado en la DB.
            if (conversation.Mode != ConversationMode.Automatic) return (false, conversation, null);

            // 3. Reglas Deterministas (Level 0 Fast Rules) - Para ahorrar consumo de IA
            var txt = request.MessageText.Trim().ToLowerInvariant();
            var wordCount = txt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            string? fastResponse = null;
            if (wordCount <= 3 && Regex.IsMatch(txt, @"^(hola|buenas|ola|buenos dias|buenas tardes|hey)$"))
            {
                // Solo borramos el contexto si nos saludan con un mensaje nuevo de inicio.
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

            // Si llegó hasta aquí, la IA (Orquestador) debe pensar y responder.
            return (true, conversation, null);
        }
        finally
        {
            stateLock.Release();
        }
    }
}
