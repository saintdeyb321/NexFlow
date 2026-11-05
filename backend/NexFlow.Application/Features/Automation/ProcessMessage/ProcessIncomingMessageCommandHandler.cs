using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.AgentV2;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

public class ProcessIncomingMessageCommandHandler
{
    private readonly IContextRecoveryService _contextRecovery;
    private readonly ILocationRepository _locationRepo;
    private readonly IEntitlementService _entitlementService;
    private readonly IIncomingMessageGuard _guard;
    private readonly IConversationStateService _stateService;
    private readonly IAiResponseOrchestrator _aiOrchestrator;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IOutboundMessageService _outboundMessageService;
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;
    private readonly IClock? _clock;
    private readonly IExperimentalAgentRuntime? _experiment;
    private readonly IAgentDemoPolicy? _demoPolicy;
    private readonly IAgentDemoChannel? _demoChannel;
    private readonly IAgentV2Ingress? _agentV2;

    public ProcessIncomingMessageCommandHandler(
        IIncomingMessageGuard guard,
        IConversationStateService stateService,
        IAiResponseOrchestrator aiOrchestrator,
        IKnowledgeService knowledgeService,
        IOutboundMessageService outboundMessageService,
        ILogger<ProcessIncomingMessageCommandHandler> logger,
        IContextRecoveryService contextRecovery,
        ILocationRepository locationRepo,
        IEntitlementService entitlementService, IClock? clock = null, IExperimentalAgentRuntime? experiment = null,
        IAgentDemoPolicy? demoPolicy = null, IAgentDemoChannel? demoChannel = null, IAgentV2Ingress? agentV2 = null)
    {
        _guard = guard;
        _stateService = stateService;
        _aiOrchestrator = aiOrchestrator;
        _knowledgeService = knowledgeService;
        _outboundMessageService = outboundMessageService;
        _logger = logger;
        _contextRecovery = contextRecovery;
        _locationRepo = locationRepo;
        _entitlementService = entitlementService;
        _clock = clock;
        _experiment = experiment;
        _demoPolicy = demoPolicy; _demoChannel = demoChannel;
        _agentV2 = agentV2;
    }

    public Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
        => ConversationDiagnostics.MeasureAsync(_logger, "Turn", () => HandleCoreAsync(request, cancellationToken));

    private async Task<Result> HandleCoreAsync(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var guardResult = await _guard.CheckMessageAsync(request, cancellationToken);
        if (!guardResult.IsValid) { Trace("GuardRejected", guardResult.WorkspaceId); return Result.Success(); }
        // Reactions/key/protocol events are durable input, not consumer questions or human takeovers.
        if (request.Kind == IncomingMessageKind.Control) { Trace("ControlEvent", guardResult.WorkspaceId); return Result.Success(); }
        if (!request.FromMe && _agentV2 != null && await _agentV2.TryHandleAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, cancellationToken))
            return Result.Success(); // Engine selection precedes every legacy linguistic decision.

        var protectedDemo = !request.FromMe && _demoPolicy?.Protects(guardResult.WorkspaceId) == true;
        if (protectedDemo) {
            if (_demoPolicy?.Allows(guardResult.WorkspaceId, request.InstanceName, guardResult.NormalizedPhone) != true) return Result.Success();
            if (_demoChannel == null) throw new InvalidOperationException("DEMO channel is unavailable; legacy fallback is forbidden.");
            var recovered = await _demoChannel.ResumeAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, cancellationToken);
            if (recovered != null) { EnsureOutboundFinalized(recovered); return Result.Success(); }
        }
        if (!request.FromMe && !protectedDemo && await _outboundMessageService.TryResumeResponseAsync(guardResult.WorkspaceId, request.MessageId, cancellationToken))
        {
            Trace("ExistingDurableResponse", guardResult.WorkspaceId);
            return Result.Success();
        }

        var stateResult = await _stateService.ProcessStateAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, cancellationToken);
        Trace(stateResult.Record.Mode == ConversationMode.Human ? "HumanControl" : stateResult.ShouldAiRespond ? "AutomaticResponseRequired" : "NoAutomaticResponse", guardResult.WorkspaceId);

        if (protectedDemo && request.Kind == IncomingMessageKind.Text) {
            if (stateResult.ShouldAiRespond && stateResult.Record.Mode == ConversationMode.Automatic) {
                var response = await _aiOrchestrator.RespondAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, stateResult.Record, cancellationToken);
                EnsureOutboundFinalized(response);
            }
            return Result.Success();
        }

        if (!request.FromMe && request.Kind == IncomingMessageKind.Text && stateResult.ShouldAiRespond
            && stateResult.Record.Mode == ConversationMode.Automatic && _experiment?.Handles(guardResult.WorkspaceId) == true)
        {
            await _aiOrchestrator.RespondAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, stateResult.Record, cancellationToken);
            return Result.Success(); // Only reachable inside a trusted synthetic harness scope.
        }
        if (stateResult.FastReply != null)
        {
            // 🔥 SPRINT 02: Inyectamos request.MessageId como clave de idempotencia
            var outbound = await _outboundMessageService.SendMessageAsync(guardResult.WorkspaceId, stateResult.Record.Id, guardResult.NormalizedPhone, stateResult.FastReply, SenderType.AI, request.MessageId, cancellationToken);
            EnsureOutboundFinalized(outbound);
            return Result.Success();
        }

        if (stateResult.ShouldAiRespond)
        {
            // 🔥 SPRINT 02: Pasamos request.MessageId al método privado
            if (!await TryHandleZeroTokenKnowledgeAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request.MessageText, request.MessageId, stateResult.Record, cancellationToken))
            {
                var outbound = await _aiOrchestrator.RespondAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, stateResult.Record, cancellationToken);
                EnsureOutboundFinalized(outbound);
            }
        }

        return Result.Success(); // El Worker de DB marcará esto como Processed.
    }

    private void Trace(string outcome, Guid? workspaceId)
    {
        try { _logger.LogInformation("HandlerOutcome={Outcome} WorkspaceId={WorkspaceId}", outcome, workspaceId); }
        catch { /* Do not change a business outcome when diagnostics are unavailable. */ }
    }

    private async Task<bool> TryHandleZeroTokenKnowledgeAsync(Guid workspaceId, string phone, string message, string sourceMessageId, ConversationRecord conversation, CancellationToken ct)
    {
        if (message.Length > 8000) return false;
        // Only unambiguous complete shortcuts bypass recovery/handoff interpretation.
        var topic = InformationalRequest.ShortcutTopic(ConversationRecoveryService.Normalize(message));

        if (topic.HasValue)
        {
            var context = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, phone, ct);
            if (context.Focus?.PendingDiscard != null) return false;
            if (!string.IsNullOrWhiteSpace(context.CurrentGoal) || context.ReservationManagement != null) return false;
            var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var intent = topic == KnowledgeTopic.Locations ? ConversationIntent.Location : ConversationIntent.BusinessHours;
            var denied = AiIntentAccess.GetDeniedResponse(intent, modules);
            if (denied != null)
            {
                var response = await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, phone,
                    denied, SenderType.AI, sourceMessageId, ct);
                EnsureOutboundFinalized(response);
                return true;
            }
            // Once a scope/clarification exists, use recovery's clock, expiry and selection policy.
            if (topic == KnowledgeTopic.BusinessHours && context.Information != null) return false;
            KnowledgeResult result;
            try
            {
                var locations = (await _locationRepo.GetLocationsAsync(workspaceId, ct)).ToList();
                var mentionedLocations = locations.Where(l => !string.IsNullOrWhiteSpace(l.Name) && message.Contains(l.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (mentionedLocations.Count > 1) return false;

                var locationId = mentionedLocations.Count == 1 ? mentionedLocations[0].Id
                    : locations.Any(l => l.Id == context.SelectedLocationId) ? context.SelectedLocationId : null;
                var snapshot = new BusinessKnowledgeSnapshot { WorkspaceId = workspaceId, CurrentQueryLocations = locations };
                result = await _knowledgeService.QueryAsync(workspaceId, snapshot,
                    new KnowledgeQuery { Topic = topic.Value, LocationId = locationId }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Trace("KnowledgeUnavailable:" + ex.GetType().Name, workspaceId);
                result = new KnowledgeResult { Status = KnowledgeStatus.Unavailable, Source = topic.Value };
            }

            var now = _clock?.UtcNow ?? DateTime.UtcNow;
            InformationalRequest.Remember(context, topic.Value, result, now);
            ConversationQuestion.Remember(workspaceId, sourceMessageId, context, ExpectedReply.Information, intent, result.ToResponse(), now, conversation.Id);
            await _contextRecovery.SaveContextAsync(workspaceId, phone, context, ct);
            var outbound = await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, phone,
                result.ToResponse(), SenderType.AI, sourceMessageId, ct);
            EnsureOutboundFinalized(outbound);
            return true;
        }
        return false;
    }

    private static void EnsureOutboundFinalized(MessageRecord message)
    {
        if (message.Status is not (MessageStatus.Sent or MessageStatus.UnknownDelivery or MessageStatus.Failed))
            throw new InvalidOperationException("El resultado del envío aún no está persistido.");
    }
}
