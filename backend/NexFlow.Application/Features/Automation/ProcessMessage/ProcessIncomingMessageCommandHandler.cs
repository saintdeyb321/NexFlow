using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

public class ProcessIncomingMessageCommandHandler
{
    private readonly IIncomingMessageGuard _guard;
    private readonly IConversationStateService _stateService;
    private readonly IAiResponseOrchestrator _aiOrchestrator;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IMessageGateway _messageGateway;
    private readonly IConversationRepository _conversationRepo;
    private readonly IProcessedMessageRepository _processedMessageRepo;
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;

    public ProcessIncomingMessageCommandHandler(
        IIncomingMessageGuard guard,
        IConversationStateService stateService,
        IAiResponseOrchestrator aiOrchestrator,
        IKnowledgeService knowledgeService,
        IMessageGateway messageGateway,
        IConversationRepository conversationRepo,
        IProcessedMessageRepository processedMessageRepo,
        ILogger<ProcessIncomingMessageCommandHandler> logger)
    {
        _guard = guard;
        _stateService = stateService;
        _aiOrchestrator = aiOrchestrator;
        _knowledgeService = knowledgeService;
        _messageGateway = messageGateway;
        _conversationRepo = conversationRepo;
        _processedMessageRepo = processedMessageRepo;
        _logger = logger;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var guardResult = await _guard.CheckMessageAsync(request, cancellationToken);
        if (!guardResult.IsValid) return Result.Success();

        try
        {
            var stateResult = await _stateService.ProcessStateAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, cancellationToken);
            if (stateResult.FastReply != null)
            {
                await PersistAndSendAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, stateResult.Record.Id, stateResult.FastReply, cancellationToken);
                await _processedMessageRepo.MarkAsProcessedAsync(guardResult.WorkspaceId, request.MessageId, cancellationToken);
                return Result.Success();
            }

            if (stateResult.ShouldAiRespond)
            {
                if (!await TryHandleZeroTokenKnowledgeAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request.MessageText, stateResult.Record, cancellationToken))
                {
                    await _aiOrchestrator.RespondAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, stateResult.Record, cancellationToken);
                }
            }

            // 🔥 SPRINT 1: Marcamos el mensaje como procesado exitosamente al final del flujo
            await _processedMessageRepo.MarkAsProcessedAsync(guardResult.WorkspaceId, request.MessageId, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico procesando el mensaje {MessageId}. Marcando como FAILED para permitir reintento.", request.MessageId);

            // 🔥 SPRINT 1: El mensaje queda documentado como fallido y habilitado para reintentos
            await _processedMessageRepo.MarkAsFailedAsync(guardResult.WorkspaceId, request.MessageId, ex.Message, CancellationToken.None);
            throw;
        }
    }

    private async Task<bool> TryHandleZeroTokenKnowledgeAsync(Guid workspaceId, string phone, string message, ConversationRecord conversation, CancellationToken ct)
    {
        var normalizedText = message.ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
            .Replace("¿", "").Replace("?", "").Trim();

        KnowledgeTopic? topic = null;

        var locationKeywords = new[] { "donde estan", "ubicacion", "direccion", "sedes", "sucursal", "como llego", "donde atienden" };
        var hoursKeywords = new[] { "horario", "a que hora", "dias de atencion", "cuando abren", "hasta que hora", "cuando cierran" };

        if (locationKeywords.Any(k => normalizedText.Contains(k)))
        {
            topic = KnowledgeTopic.Locations;
        }
        else if (hoursKeywords.Any(k => normalizedText.Contains(k)))
        {
            topic = KnowledgeTopic.BusinessHours;
        }

        if (topic.HasValue)
        {
            var snapshot = await _knowledgeService.GetSnapshotAsync(workspaceId, ct);
            var result = _knowledgeService.Query(snapshot, new KnowledgeQuery { Topic = topic.Value });

            if (result.Found)
            {
                string reply = $"Aquí tienes la información solicitada:\n\n{result.Facts}\n¿En qué más te puedo ayudar?";
                await PersistAndSendAsync(workspaceId, phone, conversation.Id, reply, ct);
                return true;
            }
        }

        return false;
    }

    private async Task PersistAndSendAsync(Guid workspaceId, string phone, string conversationId, string text, CancellationToken ct)
    {
        var pendingId = Guid.NewGuid().ToString();
        await _conversationRepo.AddMessageAsync(workspaceId, conversationId, new MessageRecord { Id = pendingId, ExternalMessageId = pendingId, Direction = "outbound", Sender = SenderType.AI, Content = text, Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow }, ct);
        try
        {
            var extId = await _messageGateway.SendTextAsync(workspaceId, phone, text, pendingId, ct);
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Sent, extId, ct);
        }
        catch
        {
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Failed, null, ct);
        }
    }
}