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
using System;
using System.Threading;
using System.Threading.Tasks;

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
        // 1. IDEMPOTENCIA
        bool isFirstTime = await _conversationCache.TryAcquireMessageLockAsync(request.MessageId, cancellationToken);
        if (!isFirstTime) return Result.Success();

        // 2. RESOLVER INSTANCIA
        var resolvedWorkspaceId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedWorkspaceId == null || resolvedWorkspaceId == Guid.Empty) return Result.Success();
        Guid workspaceId = resolvedWorkspaceId.Value;

        // BLINDAJE 1: Ignorar grupos de WhatsApp
        if (request.CustomerPhone.Contains("@g.us") || request.CustomerPhone.Contains("-")) return Result.Success();

        // 3. CONSULTAR ESTADO PREVIO (Para proteger la privacidad del dueño)
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, request.CustomerPhone, cancellationToken);

        // BLINDAJE 2 (El parche de la mamá): Si el dueño manda un mensaje a alguien que no está en el sistema, lo ignoramos.
        if (request.FromMe && conversation == null)
        {
            _logger.LogInformation("Mensaje saliente a un número no registrado. Ignorando para proteger privacidad de contactos personales.");
            return Result.Success();
        }

        // 4. IDENTIDAD DEL CONSUMIDOR (Upsert)
        var consumer = new ConsumerIdentityRecord
        {
            Phone = request.CustomerPhone,
            DisplayName = request.CustomerName,
            FirstSeenAt = DateTime.UtcNow,
            LastInteractionAt = DateTime.UtcNow
        };
        await _consumerRepo.UpsertConsumerAsync(workspaceId, consumer, cancellationToken);

        // 5. HILO DE CONVERSACIÓN
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

        // 6. REGISTRAR MENSAJE
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

        // 7. EL ESCUDO HUMANO
        if (conversation.Mode == ConversationMode.Human || conversation.Mode == ConversationMode.Paused || request.FromMe)
        {
            _logger.LogInformation("Conversación {ConvId} en Modo Humano/Pausado o mensaje saliente. IA Silenciada.", conversation.Id);
            return Result.Success();
        }

        // 8. VERIFICAR LICENCIA Y MÓDULOS
        if (!await _entitlementService.IsLicenseValidAsync(workspaceId, cancellationToken)) return Result.Success();

        // 9. CORE AUTOMATION
        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);
        if (!intentResult.IsConfident()) intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        intentResult.Parameters["phone"] = request.CustomerPhone;
        intentResult.Parameters["messageId"] = request.MessageId;

        var systemContext = await _moduleDispatcher.BuildSystemContextAsync(workspaceId, intentResult, cancellationToken);
        await _conversationCache.SetLastIntentAsync(workspaceId, request.CustomerPhone, intentResult.Intent.ToString(), cancellationToken);

        var finalResponse = await _aiRouter.GenerateResponseAsync(workspaceId, intentResult, systemContext, cancellationToken);

        await _messageGateway.SendTextAsync(workspaceId, request.CustomerPhone, finalResponse, cancellationToken);

        // 10. REGISTRAR RESPUESTA IA
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