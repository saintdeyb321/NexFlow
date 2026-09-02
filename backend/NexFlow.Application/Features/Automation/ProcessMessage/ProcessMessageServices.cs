using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Dispatcher;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

// --- 1. GUARDIA DE ENTRADA ---
public interface IIncomingMessageGuard { Task<(bool IsValid, Guid WorkspaceId, string NormalizedPhone)> CheckMessageAsync(ProcessIncomingMessageCommand request, CancellationToken cancellationToken); }

public class IncomingMessageGuard : IIncomingMessageGuard
{
    private readonly IInstanceResolver _instanceResolver;
    private readonly IProcessedMessageRepository _processedMessageRepo;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<IncomingMessageGuard> _logger;

    public IncomingMessageGuard(IInstanceResolver instanceResolver, IProcessedMessageRepository processedMessageRepo, IEntitlementService entitlementService, ILogger<IncomingMessageGuard> logger)
    {
        _instanceResolver = instanceResolver; _processedMessageRepo = processedMessageRepo; _entitlementService = entitlementService; _logger = logger;
    }

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Contains("@g.us") || phone.Contains("status@broadcast")) return string.Empty;
        var clean = new string(phone.Split('@')[0].Where(char.IsDigit).ToArray());
        return (clean.Length >= 10 && clean.Length <= 15) ? "+" + clean : string.Empty;
    }

    public async Task<(bool IsValid, Guid WorkspaceId, string NormalizedPhone)> CheckMessageAsync(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var resolvedId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedId == null || resolvedId == Guid.Empty) return (false, Guid.Empty, string.Empty);
        if (!await _processedMessageRepo.TryAcquireLockAsync(resolvedId.Value, request.MessageId, cancellationToken)) return (false, Guid.Empty, string.Empty);
        var normalizedPhone = NormalizePhone(request.CustomerPhone);
        if (string.IsNullOrEmpty(normalizedPhone)) return (false, Guid.Empty, string.Empty);
        if (!await _entitlementService.IsLicenseValidAsync(resolvedId.Value, cancellationToken)) return (false, Guid.Empty, string.Empty);
        return (true, resolvedId.Value, normalizedPhone);
    }
}

// --- 2. GESTOR DE ESTADO ---
public interface IConversationStateService { Task<(bool ShouldAiRespond, ConversationRecord Record)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken); }

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
            if (conversation == null) return (false, null!);

            bool isAiMessage = await _conversationCache.IsMessageAiGeneratedAsync(workspaceId, request.MessageId, cancellationToken);
            if (isAiMessage) return (false, conversation);

            if (conversation.Mode != ConversationMode.Human)
            {
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, HandoffReason.ManualIntervention, cancellationToken);
            }

            await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord
            {
                Id = request.MessageId,
                Direction = "outbound",
                Sender = SenderType.BusinessUser,
                Content = request.MessageText,
                ExternalMessageId = request.MessageId,
                Status = MessageStatus.Sent,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);

            return (false, conversation);
        }

        await _consumerRepo.UpsertConsumerAsync(workspaceId, new ConsumerIdentityRecord { Phone = normalizedPhone, DisplayName = request.CustomerName, FirstSeenAt = DateTime.UtcNow, LastInteractionAt = DateTime.UtcNow }, cancellationToken);
        conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);

        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord
        {
            Id = request.MessageId,
            Direction = "inbound",
            Sender = SenderType.Consumer,
            Content = request.MessageText,
            ExternalMessageId = request.MessageId,
            Status = MessageStatus.Sent,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        bool shouldRespond = conversation.Mode != ConversationMode.Human && conversation.Mode != ConversationMode.Paused;
        return (shouldRespond, conversation);
    }
}

// --- 3. ORQUESTADOR IA ---
public interface IAiResponseOrchestrator { Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken); }

public class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly IIntentEngine _intentEngine;
    private readonly IModuleDispatcher _moduleDispatcher;
    private readonly IAiRouter _aiRouter;
    private readonly IConversationRepository _conversationRepo;
    private readonly IConversationCache _conversationCache;
    private readonly IMessageGateway _messageGateway;

    public AiResponseOrchestrator(IIntentEngine intentEngine, IModuleDispatcher moduleDispatcher, IAiRouter aiRouter, IConversationRepository conversationRepo, IConversationCache conversationCache, IMessageGateway messageGateway)
    {
        _intentEngine = intentEngine; _moduleDispatcher = moduleDispatcher; _aiRouter = aiRouter; _conversationRepo = conversationRepo; _conversationCache = conversationCache; _messageGateway = messageGateway;
    }

    public async Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        var context = await _conversationCache.GetContextAsync(workspaceId, normalizedPhone, cancellationToken);
        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, context, cancellationToken);
        if (!intentResult.IsConfident()) intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        string finalResponse;
        if (intentResult.Intent == IntentType.ProviderUnavailable) finalResponse = "Lo siento, mi sistema está experimentando una alta demanda. ¿Podrías intentar enviarme tu solicitud en un par de minutos?";
        else if (intentResult.Intent == IntentType.GeneralGreeting) finalResponse = "¡Hola! Soy el asistente virtual. ¿En qué te puedo ayudar el día de hoy?";
        else
        {
            intentResult.Parameters["phone"] = normalizedPhone;
            intentResult.Parameters["messageId"] = request.MessageId;
            var systemContext = await _moduleDispatcher.BuildSystemContextAsync(workspaceId, intentResult, cancellationToken);

            if(systemContext.RequiresHuman)
            {
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, HandoffReason.AiEscalation, cancellationToken);
            }

            if (systemContext.ModuleCode == "CORE" || !systemContext.Success)
            {
                finalResponse = systemContext.Data?.ToString() ?? "Operación no disponible en este momento.";
            }
            else
            {
                finalResponse = await _aiRouter.GenerateResponseAsync(workspaceId, systemContext, cancellationToken);
            }
        }

        var pendingId = Guid.NewGuid().ToString();
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord
        {
            Id = pendingId,
            ExternalMessageId = pendingId,
            Direction = "outbound",
            Sender = SenderType.AI,
            Content = finalResponse,
            Status = MessageStatus.Pending,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        try
        {
            var extId = await _messageGateway.SendTextAsync(workspaceId, normalizedPhone, finalResponse, cancellationToken);
            await _conversationCache.MarkMessageAsAiGeneratedAsync(workspaceId, extId, cancellationToken);
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversation.Id, pendingId, MessageStatus.Sent, extId, cancellationToken);
        }
        catch
        {
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversation.Id, pendingId, MessageStatus.Failed, null, cancellationToken);
            throw;
        }
    }
}