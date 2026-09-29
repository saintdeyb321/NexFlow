using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

public class ProcessIncomingMessageCommandHandler
{
    private readonly IContextRecoveryService _contextRecovery;
    private readonly ILocationRepository _locationRepo;
    private readonly IIncomingMessageGuard _guard;
    private readonly IConversationStateService _stateService;
    private readonly IAiResponseOrchestrator _aiOrchestrator;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IOutboundMessageService _outboundMessageService;
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;

    public ProcessIncomingMessageCommandHandler(
        IIncomingMessageGuard guard,
        IConversationStateService stateService,
        IAiResponseOrchestrator aiOrchestrator,
        IKnowledgeService knowledgeService,
        IOutboundMessageService outboundMessageService,
        ILogger<ProcessIncomingMessageCommandHandler> logger,
        IContextRecoveryService contextRecovery,
        ILocationRepository locationRepo)
    {
        _guard = guard;
        _stateService = stateService;
        _aiOrchestrator = aiOrchestrator;
        _knowledgeService = knowledgeService;
        _outboundMessageService = outboundMessageService;
        _logger = logger;
        _contextRecovery = contextRecovery;
        _locationRepo = locationRepo;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var guardResult = await _guard.CheckMessageAsync(request, cancellationToken);
        if (!guardResult.IsValid) return Result.Success();

        var stateResult = await _stateService.ProcessStateAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, cancellationToken);

        if (stateResult.FastReply != null)
        {
            // 🔥 SPRINT 02: Inyectamos request.MessageId como clave de idempotencia
            var outbound = await _outboundMessageService.SendMessageAsync(guardResult.WorkspaceId, stateResult.Record.Id, guardResult.NormalizedPhone, stateResult.FastReply, SenderType.AI, request.MessageId, cancellationToken);
            EnsureMessageSent(outbound);
            return Result.Success();
        }

        if (stateResult.ShouldAiRespond)
        {
            // 🔥 SPRINT 02: Pasamos request.MessageId al método privado
            if (!await TryHandleZeroTokenKnowledgeAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request.MessageText, request.MessageId, stateResult.Record, cancellationToken))
            {
                var outbound = await _aiOrchestrator.RespondAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, stateResult.Record, cancellationToken);
                EnsureMessageSent(outbound);
            }
        }

        return Result.Success(); // El Worker de DB marcará esto como Processed.
    }

    private async Task<bool> TryHandleZeroTokenKnowledgeAsync(Guid workspaceId, string phone, string message, string sourceMessageId, ConversationRecord conversation, CancellationToken ct)
    {
        var normalizedText = message.ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
            .Replace("¿", "").Replace("?", "").Trim();

        KnowledgeTopic? topic = null;
        var locationKeywords = new[] { "donde estan", "ubicacion", "direccion", "sedes", "sucursal", "como llego", "donde atienden" };
        var hoursKeywords = new[] { "horario", "a que hora", "dias de atencion", "cuando abren", "hasta que hora", "cuando cierran" };

        if (locationKeywords.Any(k => normalizedText.Contains(k))) topic = KnowledgeTopic.Locations;
        else if (hoursKeywords.Any(k => normalizedText.Contains(k))) topic = KnowledgeTopic.BusinessHours;

        if (topic.HasValue)
        {
            var context = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, phone, ct);
            var locations = (await _locationRepo.GetLocationsAsync(workspaceId, ct)).ToList();
            var mentionedLocations = locations.Where(l => !string.IsNullOrWhiteSpace(l.Name) && message.Contains(l.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mentionedLocations.Count > 1) return false;

            var locationId = mentionedLocations.Count == 1 ? mentionedLocations[0].Id : context.SelectedLocationId;
            if (!string.IsNullOrWhiteSpace(locationId) && !locations.Any(l => l.Id == locationId)) return false;

            var snapshot = new BusinessKnowledgeSnapshot { WorkspaceId = workspaceId };
            var result = await _knowledgeService.QueryAsync(workspaceId, snapshot, new KnowledgeQuery { Topic = topic.Value, LocationId = locationId }, ct);

            if (result.Found)
            {
                string reply = $"Aquí tienes la información solicitada:\n\n{result.Facts}\n¿En qué más te puedo ayudar?";

                // 🔥 SPRINT 02: Inyectamos sourceMessageId como clave de idempotencia
                var outbound = await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, phone, reply, SenderType.AI, sourceMessageId, ct);
                EnsureMessageSent(outbound);
                return true;
            }
        }
        return false;
    }

    private static void EnsureMessageSent(MessageRecord message)
    {
        if (message.Status != MessageStatus.Sent)
            throw new InvalidOperationException("El envío de la respuesta no fue confirmado.");
    }
}