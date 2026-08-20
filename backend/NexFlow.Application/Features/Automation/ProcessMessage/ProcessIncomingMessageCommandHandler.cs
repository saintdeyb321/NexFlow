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
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;

    public ProcessIncomingMessageCommandHandler(
        IIntentEngine intentEngine,
        IAiRouter aiRouter,
        IMessageGateway messageGateway,
        IEntitlementService entitlementService,
        IConversationCache conversationCache,
        IModuleDispatcher moduleDispatcher,
        ILogger<ProcessIncomingMessageCommandHandler> logger)
    {
        _intentEngine = intentEngine;
        _aiRouter = aiRouter;
        _messageGateway = messageGateway;
        _entitlementService = entitlementService;
        _conversationCache = conversationCache;
        _moduleDispatcher = moduleDispatcher;
        _logger = logger;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // 1. Seguridad: Licencia Activa
        if (!await _entitlementService.IsLicenseValidAsync(request.WorkspaceId, cancellationToken))
        {
            _logger.LogWarning("Mensaje rechazado. Workspace {Workspace} inactivo.", request.WorkspaceId);
            return Result.Failure(new Error("License.Invalid", "Licencia inactiva."));
        }

        // 2. IA: Analizar intención
        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);
        if (!intentResult.IsConfident()) intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        // 3. Despachador: Construir contexto cruzando bases de datos reales
        var systemContext = await _moduleDispatcher.BuildSystemContextAsync(request.WorkspaceId, intentResult, cancellationToken);

        // 4. Redis: Guardar contexto temporal
        await _conversationCache.SetLastIntentAsync(request.WorkspaceId, request.CustomerPhone, intentResult.Intent.ToString(), cancellationToken);

        // 5. IA: Generar respuesta humana
        var finalResponse = await _aiRouter.GenerateResponseAsync(request.WorkspaceId, intentResult, systemContext, cancellationToken);

        // 6. WhatsApp: Enviar respuesta
        await _messageGateway.SendTextAsync(request.WorkspaceId, request.CustomerPhone, finalResponse, cancellationToken);

        return Result.Success();
    }
}