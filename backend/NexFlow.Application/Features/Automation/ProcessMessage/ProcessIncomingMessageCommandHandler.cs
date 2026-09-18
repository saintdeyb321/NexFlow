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

    public ProcessIncomingMessageCommandHandler(
        IIncomingMessageGuard guard,
        IConversationStateService stateService,
        IAiResponseOrchestrator aiOrchestrator,
        IKnowledgeService knowledgeService,
        IMessageGateway messageGateway,
        IConversationRepository conversationRepo)
    {
        _guard = guard;
        _stateService = stateService;
        _aiOrchestrator = aiOrchestrator;
        _knowledgeService = knowledgeService;
        _messageGateway = messageGateway;
        _conversationRepo = conversationRepo;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // 1. Filtrar, Validar Idempotencia y Licencia
        var guardResult = await _guard.CheckMessageAsync(request, cancellationToken);
        if (!guardResult.IsValid) return Result.Success();

        // 2. Gestionar Estado (Intervención humana, guardar consumidor y mensaje)
        var stateResult = await _stateService.ProcessStateAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, cancellationToken);
        if (stateResult.FastReply != null)
        {
            await PersistAndSendAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, stateResult.Record.Id, stateResult.FastReply, cancellationToken);
            return Result.Success();
        }

        if (!stateResult.ShouldAiRespond) return Result.Success();

        // 3. 🔥 SPRINT 14: INTERCEPTOR ZERO-TOKEN KNOWLEDGE
        // Si es una pregunta estática simple, respondemos en 0ms sin gastar tokens de Gemini.
        if (await TryHandleZeroTokenKnowledgeAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request.MessageText, stateResult.Record, cancellationToken))
        {
            return Result.Success();
        }

        // 4. Si la consulta es compleja o es una transacción (reserva), orquestamos a Gemini
        await _aiOrchestrator.RespondAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, stateResult.Record, cancellationToken);

        return Result.Success();
    }

    private async Task<bool> TryHandleZeroTokenKnowledgeAsync(Guid workspaceId, string phone, string message, ConversationRecord conversation, CancellationToken ct)
    {
        var text = message.ToLowerInvariant();
        KnowledgeTopic? topic = null;

        // Heurística de Reglas Rápidas (Fast Rules)
        if (text.Contains("donde estan") || text.Contains("ubicacion") || text.Contains("direccion") || text.Contains("sedes"))
        {
            topic = KnowledgeTopic.Locations;
        }
        else if (text.Contains("horario") || text.Contains("a que hora") || text.Contains("dias de atencion"))
        {
            topic = KnowledgeTopic.BusinessHours;
        }

        // Si detectamos un tópico simple, disparamos el Snapshot
        if (topic.HasValue)
        {
            var snapshot = await _knowledgeService.GetSnapshotAsync(workspaceId, ct);
            var result = _knowledgeService.Query(snapshot, new KnowledgeQuery { Topic = topic.Value });

            if (result.Found)
            {
                string reply = $"Aquí tienes la información solicitada:\n\n{result.Facts}\n¿En qué más te puedo ayudar?";
                await PersistAndSendAsync(workspaceId, phone, conversation.Id, reply, ct);
                return true; // Se interceptó con éxito. Gemini nunca se enteró.
            }
        }

        return false; // La pregunta es compleja, que trabaje la IA.
    }

    private async Task PersistAndSendAsync(Guid workspaceId, string phone, string conversationId, string text, CancellationToken ct)
    {
        var pendingId = Guid.NewGuid().ToString();
        await _conversationRepo.AddMessageAsync(workspaceId, conversationId, new MessageRecord { Id = pendingId, ExternalMessageId = pendingId, Direction = "outbound", Sender = SenderType.AI, Content = text, Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow }, ct);
        try
        {
            var extId = await _messageGateway.SendTextAsync(workspaceId, phone, text, ct);
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Sent, extId, ct);
        }
        catch
        {
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Failed, null, ct);
        }
    }
}