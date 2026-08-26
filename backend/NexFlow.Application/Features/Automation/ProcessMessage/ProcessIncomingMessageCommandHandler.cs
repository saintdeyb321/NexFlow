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
        // 1. RESOLVER INSTANCIA PRIMERO (El origen del Tenant)
        var resolvedWorkspaceId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedWorkspaceId == null || resolvedWorkspaceId == Guid.Empty) return Result.Success();
        Guid workspaceId = resolvedWorkspaceId.Value;

        // 2. IDEMPOTENCIA BLINDADA (WorkspaceId + ExternalMessageId)
        // 🔥 CORRECCIÓN: La llave ahora es absolutamente única por inquilino.
        string idempotencyKey = $"{workspaceId}_{request.MessageId}";
        bool isFirstTime = await _conversationCache.TryAcquireMessageLockAsync(workspaceId, idempotencyKey, cancellationToken);

        if (!isFirstTime)
        {
            _logger.LogInformation("Mensaje duplicado interceptado (Idempotencia): {Key}", idempotencyKey);
            return Result.Success();
        }

        // 3. BLINDAJE 1: Ignorar grupos de WhatsApp de inmediato
        if (request.CustomerPhone.Contains("@g.us") || request.CustomerPhone.Contains("-"))
            return Result.Success();

        // 4. CONSULTAR ESTADO PREVIO DE LA CONVERSACIÓN
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, request.CustomerPhone, cancellationToken);

        // 5. BLINDAJE 2 (Privacidad): Ignorar mensajes salientes del dueño a contactos personales no registrados.
        if (request.FromMe && conversation == null)
        {
            _logger.LogInformation("Mensaje saliente a un número no registrado. Ignorando para proteger privacidad del usuario.");
            return Result.Success();
        }

        // 6. IDENTIDAD DEL CONSUMIDOR (Upsert)
        var consumer = new ConsumerIdentityRecord
        {
            Phone = request.CustomerPhone,
            DisplayName = request.CustomerName,
            FirstSeenAt = DateTime.UtcNow,
            LastInteractionAt = DateTime.UtcNow
        };
        await _consumerRepo.UpsertConsumerAsync(workspaceId, consumer, cancellationToken);

        // 7. CREACIÓN DEL HILO DE CONVERSACIÓN (Si no existe)
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

        // 8. REGISTRAR EL MENSAJE (Entrante del cliente o Saliente del dueño)
        var messageRecord = new MessageRecord
        {
            Id = request.MessageId,
            Direction = request.FromMe ? "outbound" : "inbound",
            Sender = request.FromMe ? SenderType.BusinessUser : SenderType.Consumer,
            Content = request.MessageText,
            ExternalMessageId = request.MessageId, // Fundamental para rastrear en Evolution
            Timestamp = DateTime.UtcNow
        };
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, messageRecord, cancellationToken);

        // 9. EL ESCUDO DE LA IA (Handoff / Pause)
        // 🔥 CORRECCIÓN: Si el dueño intervino desde WhatsApp Web (FromMe), silenciamos a la IA.
        if (conversation.Mode == ConversationMode.Human || conversation.Mode == ConversationMode.Paused || request.FromMe)
        {
            _logger.LogInformation("IA Silenciada en {ConvId}. Razón: Modo {Mode} o Mensaje FromMe ({FromMe}).",
                conversation.Id, conversation.Mode, request.FromMe);
            return Result.Success();
        }

        // 10. VERIFICAR LICENCIA Y ENTITLEMENTS ANTES DE GASTAR TOKENS
        if (!await _entitlementService.IsLicenseValidAsync(workspaceId, cancellationToken))
            return Result.Success();

        // 11. CORE AUTOMATION (El cerebro de NexFlow)
        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);
        if (!intentResult.IsConfident())
            intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        intentResult.Parameters["phone"] = request.CustomerPhone;
        intentResult.Parameters["messageId"] = request.MessageId;

        var systemContext = await _moduleDispatcher.BuildSystemContextAsync(workspaceId, intentResult, cancellationToken);
        var finalResponse = await _aiRouter.GenerateResponseAsync(workspaceId, intentResult, systemContext, cancellationToken);

        // Enviar respuesta por WhatsApp
        await _messageGateway.SendTextAsync(workspaceId, request.CustomerPhone, finalResponse, cancellationToken);

        // 12. REGISTRAR LA RESPUESTA DE LA IA EN FIRESTORE
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