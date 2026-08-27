using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Engines.Dispatcher;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

public class ProcessIncomingMessageCommandHandler
{
    private readonly IIntentEngine _intentEngine;
    private readonly IAiRouter _aiRouter;
    private readonly IMessageGateway _messageGateway;
    private readonly IEntitlementService _entitlementService;
    private readonly IConversationCache _conversationCache;
    private readonly IModuleDispatcher _moduleDispatcher;
    private readonly IInstanceResolver _instanceResolver;
    private readonly IConsumerIdentityRepository _consumerRepo;
    private readonly IConversationRepository _conversationRepo;
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;

    public ProcessIncomingMessageCommandHandler(
        IIntentEngine intentEngine, IAiRouter aiRouter, IMessageGateway messageGateway,
        IEntitlementService entitlementService, IConversationCache conversationCache,
        IModuleDispatcher moduleDispatcher, IInstanceResolver instanceResolver,
        IConsumerIdentityRepository consumerRepo, IConversationRepository conversationRepo,
        ILogger<ProcessIncomingMessageCommandHandler> logger)
    {
        _intentEngine = intentEngine; _aiRouter = aiRouter; _messageGateway = messageGateway;
        _entitlementService = entitlementService; _conversationCache = conversationCache;
        _moduleDispatcher = moduleDispatcher; _instanceResolver = instanceResolver;
        _consumerRepo = consumerRepo; _conversationRepo = conversationRepo;
        _logger = logger;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var resolvedWorkspaceId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedWorkspaceId == null || resolvedWorkspaceId == Guid.Empty) return Result.Success();
        Guid workspaceId = resolvedWorkspaceId.Value;

        string idempotencyKey = $"{workspaceId}_{request.MessageId}";
        bool isFirstTime = await _conversationCache.TryAcquireMessageLockAsync(workspaceId, idempotencyKey, cancellationToken);

        if (!isFirstTime)
        {
            _logger.LogInformation("Mensaje duplicado interceptado (Idempotencia): {Key}", idempotencyKey);
            return Result.Success();
        }

        if (request.CustomerPhone.Contains("@g.us") || request.CustomerPhone.Contains("-"))
            return Result.Success();

        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, request.CustomerPhone, cancellationToken);

        if (request.FromMe && conversation == null)
            return Result.Success();

        var consumer = new ConsumerIdentityRecord
        {
            Phone = request.CustomerPhone,
            DisplayName = request.CustomerName,
            FirstSeenAt = DateTime.UtcNow,
            LastInteractionAt = DateTime.UtcNow
        };
        await _consumerRepo.UpsertConsumerAsync(workspaceId, consumer, cancellationToken);

        if (conversation == null)
        {
            conversation = new ConversationRecord
            {
                Id = Guid.NewGuid().ToString(),
                ConsumerPhone = request.CustomerPhone,
                Channel = "whatsapp",
                Mode = ConversationMode.Automatic,
                Status = "open",
                StartedAt = DateTime.UtcNow,
                LastMessageAt = DateTime.UtcNow
            };
            await _conversationRepo.CreateConversationAsync(workspaceId, conversation, cancellationToken);
        }

        var messageRecord = new MessageRecord
        {
            Id = request.MessageId,
            Direction = request.FromMe ? "outbound" : "inbound",
            Sender = request.FromMe ? SenderType.BusinessUser : SenderType.Consumer,
            Content = request.MessageText,
            ExternalMessageId = request.MessageId,
            Timestamp = DateTime.UtcNow
        };
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, messageRecord, cancellationToken);

        if (conversation.Mode == ConversationMode.Human || conversation.Mode == ConversationMode.Paused || request.FromMe)
        {
            _logger.LogInformation("IA Silenciada en {ConvId}. Razón: Modo {Mode} o Mensaje FromMe ({FromMe}).",
                conversation.Id, conversation.Mode, request.FromMe);
            return Result.Success();
        }

        if (!await _entitlementService.IsLicenseValidAsync(workspaceId, cancellationToken))
            return Result.Success();

        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);
        if (!intentResult.IsConfident())
            intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        intentResult.Parameters["phone"] = request.CustomerPhone;
        intentResult.Parameters["messageId"] = request.MessageId;

        ModuleExecutionResult systemContext = await _moduleDispatcher.BuildSystemContextAsync(workspaceId, intentResult, cancellationToken);

        if (systemContext.RequiresHuman)
        {
            await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, cancellationToken);
            _logger.LogInformation("Handoff Automático disparado para conversación {ConvId}", conversation.Id);
        }

        var finalResponse = await _aiRouter.GenerateResponseAsync(workspaceId, intentResult, systemContext, cancellationToken);

        await _messageGateway.SendTextAsync(workspaceId, request.CustomerPhone, finalResponse, cancellationToken);

        var aiMessageRecord = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            Direction = "outbound",
            Sender = SenderType.AI,
            Content = finalResponse,
            Timestamp = DateTime.UtcNow
        };
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, aiMessageRecord, cancellationToken);

        return Result.Success();
    }
}