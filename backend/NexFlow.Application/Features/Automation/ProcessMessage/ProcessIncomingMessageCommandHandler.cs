using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Engines.Dispatcher;

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
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;

    private static readonly string[] WakeWords = { "hola", "info", "precio", "reserva", "cita", "turno", "bot", "menu", "menú" };

    public ProcessIncomingMessageCommandHandler(
        IIntentEngine intentEngine, IAiRouter aiRouter, IMessageGateway messageGateway,
        IEntitlementService entitlementService, IConversationCache conversationCache,
        IModuleDispatcher moduleDispatcher, IInstanceResolver instanceResolver,
        ILogger<ProcessIncomingMessageCommandHandler> logger)
    {
        _intentEngine = intentEngine; _aiRouter = aiRouter; _messageGateway = messageGateway;
        _entitlementService = entitlementService; _conversationCache = conversationCache;
        _moduleDispatcher = moduleDispatcher; _instanceResolver = instanceResolver; _logger = logger;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // 1. IDEMPOTENCIA ATÓMICA EN REDIS (P0 Resuelto)
        bool isFirstTime = await _conversationCache.TryAcquireMessageLockAsync(request.MessageId, cancellationToken);
        if (!isFirstTime)
        {
            _logger.LogInformation("Mensaje duplicado bloqueado nativamente por Redis: {MessageId}", request.MessageId);
            return Result.Success();
        }

        // 2. RESOLVER INSTANCIA -> WORKSPACE
        var resolvedWorkspaceId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedWorkspaceId == null || resolvedWorkspaceId == Guid.Empty)
        {
            _logger.LogWarning("Instancia de Evolution no reconocida: {Instance}", request.InstanceName);
            return Result.Success();
        }

        Guid workspaceId = resolvedWorkspaceId.Value;

        // BLINDAJE: Ignorar grupos
        if (request.CustomerPhone.Contains("@g.us") || request.CustomerPhone.Contains("-")) return Result.Success();

        // BLINDAJE: Wake-word (Estrategia de Optimización)
        var lastIntent = await _conversationCache.GetLastIntentAsync(workspaceId, request.CustomerPhone, cancellationToken);
        if (string.IsNullOrEmpty(lastIntent))
        {
            var lowerMsg = request.MessageText.ToLowerInvariant();
            if (!Array.Exists(WakeWords, word => lowerMsg.Contains(word))) return Result.Success();
        }

        // BLINDAJE: Licencia
        if (!await _entitlementService.IsLicenseValidAsync(workspaceId, cancellationToken)) return Result.Success();

        // CORE AUTOMATION
        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);
        if (!intentResult.IsConfident()) intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        intentResult.Parameters["phone"] = request.CustomerPhone;
        intentResult.Parameters["messageId"] = request.MessageId;

        var systemContext = await _moduleDispatcher.BuildSystemContextAsync(workspaceId, intentResult, cancellationToken);

        await _conversationCache.SetLastIntentAsync(workspaceId, request.CustomerPhone, intentResult.Intent.ToString(), cancellationToken);

        var finalResponse = await _aiRouter.GenerateResponseAsync(workspaceId, intentResult, systemContext, cancellationToken);

        await _messageGateway.SendTextAsync(workspaceId, request.CustomerPhone, finalResponse, cancellationToken);

        return Result.Success();
    }
}