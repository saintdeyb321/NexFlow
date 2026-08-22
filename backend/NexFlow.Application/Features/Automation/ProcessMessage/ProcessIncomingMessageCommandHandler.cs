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

    // NUEVO: Repositorios Operativos
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
        // 1. IDEMPOTENCIA ATÓMICA EN REDIS
        bool isFirstTime = await _conversationCache.TryAcquireMessageLockAsync(request.MessageId, cancellationToken);
        if (!isFirstTime) return Result.Success();

        // 2. RESOLVER INSTANCIA -> WORKSPACE
        var resolvedWorkspaceId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedWorkspaceId == null || resolvedWorkspaceId == Guid.Empty) return Result.Success();
        Guid workspaceId = resolvedWorkspaceId.Value;

        // BLINDAJE: Ignorar grupos de WhatsApp
        if (request.CustomerPhone.Contains("@g.us") || request.CustomerPhone.Contains("-")) return Result.Success();

        // 3. IDENTIDAD DEL CONSUMIDOR (Mínima, Legal y en Firestore)
        var consumer = new ConsumerIdentityRecord
        {
            Phone = request.CustomerPhone,
            DisplayName = request.CustomerName, // Solo referencial, no es un CRM estricto
            FirstSeenAt = DateTime.UtcNow,
            LastInteractionAt = DateTime.UtcNow
        };
        await _consumerRepo.UpsertConsumerAsync(workspaceId, consumer, cancellationToken);

        // 4. HILO DE CONVERSACIÓN (Firestore)
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, request.CustomerPhone, cancellationToken);
        if (conversation == null)
        {
            conversation = new ConversationRecord
            {
                Id = Guid.NewGuid().ToString(),
                ConsumerPhone = request.CustomerPhone,
                Channel = "whatsapp",
                Mode = ConversationMode.Automatic, // Por defecto, la IA atiende
                Status = "open",
                StartedAt = DateTime.UtcNow,
                LastMessageAt = DateTime.UtcNow
            };
            await _conversationRepo.CreateConversationAsync(workspaceId, conversation, cancellationToken);
        }

        // 5. REGISTRAR EL MENSAJE ENTRANTE / SALIENTE (Historial Operativo)
        var messageRecord = new MessageRecord
        {
            Id = request.MessageId,
            Direction = request.FromMe ? "outbound" : "inbound",
            // Si viene del propio teléfono del negocio (FromMe), el Sender es el dueño. Si no, es el consumidor.
            Sender = request.FromMe ? SenderType.BusinessUser : SenderType.Consumer,
            Content = request.MessageText,
            ExternalMessageId = request.MessageId,
            Timestamp = DateTime.UtcNow
        };
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, messageRecord, cancellationToken);

        // ====================================================================================
        // 6. REGLA DE ORO: EL ESCUDO HUMANO
        // Si la conversación fue tomada por un humano (Mode == Human), o si el mensaje 
        // fue enviado por el propio dueño (FromMe == true), la IA SE CALLA.
        // ====================================================================================
        if (conversation.Mode == ConversationMode.Human || conversation.Mode == ConversationMode.Paused || request.FromMe)
        {
            _logger.LogInformation("Conversación {ConvId} en Modo Humano/Pausado o mensaje saliente. IA Silenciada.", conversation.Id);
            return Result.Success();
        }

        // 7. COMPROBACIÓN DE LICENCIA (SaaS Multi-tenant)
        if (!await _entitlementService.IsLicenseValidAsync(workspaceId, cancellationToken)) return Result.Success();

        // 8. CORE AUTOMATION (LA IA INTERPRETA Y EJECUTA)
        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);
        if (!intentResult.IsConfident()) intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        intentResult.Parameters["phone"] = request.CustomerPhone;
        intentResult.Parameters["messageId"] = request.MessageId;

        // Armamos contexto para el prompt
        var systemContext = await _moduleDispatcher.BuildSystemContextAsync(workspaceId, intentResult, cancellationToken);
        await _conversationCache.SetLastIntentAsync(workspaceId, request.CustomerPhone, intentResult.Intent.ToString(), cancellationToken);

        // Generamos respuesta final
        var finalResponse = await _aiRouter.GenerateResponseAsync(workspaceId, intentResult, systemContext, cancellationToken);

        // Enviamos al WhatsApp del consumidor
        await _messageGateway.SendTextAsync(workspaceId, request.CustomerPhone, finalResponse, cancellationToken);

        // 9. GUARDAR LA RESPUESTA DE LA IA EN FIRESTORE
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