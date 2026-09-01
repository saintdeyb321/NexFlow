using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories; // 🔥 Requisito para el nuevo repositorio
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
    private readonly IProcessedMessageRepository _processedMessageRepo; // 🔥 Inyección del nuevo repo
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;

    public ProcessIncomingMessageCommandHandler(
        IIntentEngine intentEngine, IAiRouter aiRouter, IMessageGateway messageGateway,
        IEntitlementService entitlementService, IConversationCache conversationCache,
        IModuleDispatcher moduleDispatcher, IInstanceResolver instanceResolver,
        IConsumerIdentityRepository consumerRepo, IConversationRepository conversationRepo,
        IProcessedMessageRepository processedMessageRepo, // 🔥 Inyección del nuevo repo
        ILogger<ProcessIncomingMessageCommandHandler> logger)
    {
        _intentEngine = intentEngine; _aiRouter = aiRouter; _messageGateway = messageGateway;
        _entitlementService = entitlementService; _conversationCache = conversationCache;
        _moduleDispatcher = moduleDispatcher; _instanceResolver = instanceResolver;
        _consumerRepo = consumerRepo; _conversationRepo = conversationRepo;
        _processedMessageRepo = processedMessageRepo; // 🔥 Inyección del nuevo repo
        _logger = logger;
    }

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return phone;
        var clean = phone.Split('@')[0];
        clean = new string(clean.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (!clean.StartsWith("+") && clean.Length >= 10) clean = "+" + clean;
        return clean;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var resolvedWorkspaceId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedWorkspaceId == null || resolvedWorkspaceId == Guid.Empty) return Result.Success();
        Guid workspaceId = resolvedWorkspaceId.Value;

        // 🔥 Auditoría: Idempotencia delegada al repositorio de PostgreSQL, no a Redis.
        bool isFirstTime = await _processedMessageRepo.TryAcquireLockAsync(workspaceId, request.MessageId, cancellationToken);

        if (!isFirstTime)
        {
            _logger.LogInformation("Mensaje duplicado interceptado (Idempotencia en BD): {MessageId}", request.MessageId);
            return Result.Success();
        }

        if (request.CustomerPhone.Contains("@g.us") || request.CustomerPhone.Contains("-"))
            return Result.Success();

        var normalizedPhone = NormalizePhone(request.CustomerPhone);
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);

        if (request.FromMe && conversation != null)
        {
            var recentMessages = await _conversationRepo.GetMessagesAsync(workspaceId, conversation.Id, 20, cancellationToken);
            bool isAiMessage = recentMessages.Any(m => m.ExternalMessageId == request.MessageId && m.Sender == SenderType.AI);

            // Lectura a la memoria caché ultrarrápida.
            if (!isAiMessage)
            {
                isAiMessage = await _conversationCache.IsMessageAiGeneratedAsync(workspaceId, request.MessageId, cancellationToken);
            }

            if (isAiMessage)
            {
                return Result.Success();
            }

            if (conversation.Mode != ConversationMode.Human)
            {
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, cancellationToken);
                _logger.LogInformation("Handoff Automático disparado por intervención manual del dueño en la conversación {ConvId}", conversation.Id);
            }
        }
        else if (request.FromMe && conversation == null)
        {
            return Result.Success();
        }

        if (!await _entitlementService.IsLicenseValidAsync(workspaceId, cancellationToken))
        {
            _logger.LogWarning("Licencia inválida. Ignorando mensaje en {WorkspaceId}.", workspaceId);
            return Result.Success();
        }

        var consumer = new ConsumerIdentityRecord
        {
            Phone = normalizedPhone,
            DisplayName = request.CustomerName,
            FirstSeenAt = DateTime.UtcNow,
            LastInteractionAt = DateTime.UtcNow
        };
        await _consumerRepo.UpsertConsumerAsync(workspaceId, consumer, cancellationToken);

        if (conversation == null)
        {
            conversation = await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);
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
            return Result.Success();
        }

        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);
        if (!intentResult.IsConfident())
            intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        string finalResponse;

        // 🔥 Auditoría: Capacidades Core Desacopladas. Saludo directo sin procesar módulos ni consumir IA.
        if (intentResult.Intent == IntentType.GeneralGreeting)
        {
            finalResponse = "¡Hola! Soy el asistente virtual. ¿En qué te puedo ayudar el día de hoy?";
        }
        else
        {
            intentResult.Parameters["phone"] = normalizedPhone;
            intentResult.Parameters["messageId"] = request.MessageId;

            ModuleExecutionResult systemContext = await _moduleDispatcher.BuildSystemContextAsync(workspaceId, intentResult, cancellationToken);

            if (systemContext.RequiresHuman)
            {
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, cancellationToken);
                _logger.LogInformation("Handoff Automático disparado por IA para conversación {ConvId}", conversation.Id);
            }

            finalResponse = await _aiRouter.GenerateResponseAsync(workspaceId, systemContext, cancellationToken);
        }

        var pendingMessageId = Guid.NewGuid().ToString();
        var aiMessageRecord = new MessageRecord
        {
            Id = pendingMessageId,
            ExternalMessageId = pendingMessageId, // Asumimos control del ID hasta que Evolution responda
            Direction = "outbound",
            Sender = SenderType.AI,
            Content = finalResponse,
            Timestamp = DateTime.UtcNow
        };
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, aiMessageRecord, cancellationToken);
        var evolutionExternalId = await _messageGateway.SendTextAsync(workspaceId, normalizedPhone, finalResponse, cancellationToken);
        await _conversationCache.MarkMessageAsAiGeneratedAsync(workspaceId, evolutionExternalId, cancellationToken);

        return Result.Success();
        }
    }