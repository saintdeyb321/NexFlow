using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Engines.Dispatcher;
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
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;

    // Palabras clave para despertar al bot si es un chat nuevo (se puede mover a BD luego)
    private static readonly string[] WakeWords = { "hola", "info", "precio", "reserva", "cita", "turno", "bot", "menu", "menú" };

    public ProcessIncomingMessageCommandHandler(
        // Inyecciones...
        IIntentEngine intentEngine, IAiRouter aiRouter, IMessageGateway messageGateway,
        IEntitlementService entitlementService, IConversationCache conversationCache,
        IModuleDispatcher moduleDispatcher, ILogger<ProcessIncomingMessageCommandHandler> logger)
    {
        _intentEngine = intentEngine; _aiRouter = aiRouter; _messageGateway = messageGateway;
        _entitlementService = entitlementService; _conversationCache = conversationCache;
        _moduleDispatcher = moduleDispatcher; _logger = logger;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // BLINDAJE 1: Ignorar mensajes de Grupos de WhatsApp (@g.us)
        if (request.CustomerPhone.Contains("@g.us") || request.CustomerPhone.Contains("-"))
        {
            _logger.LogInformation("Mensaje de grupo ignorado. Ahorrando tokens.");
            return Result.Success(); // Retornamos Success para que Evolution no reintente
        }

        // BLINDAJE 2: Verificación de Sesión Activa (Wake-word)
        var lastIntent = await _conversationCache.GetLastIntentAsync(request.WorkspaceId, request.CustomerPhone, cancellationToken);

        if (string.IsNullOrEmpty(lastIntent))
        {
            // Es un chat nuevo. Verificamos si usó una palabra clave para despertar al bot
            var lowerMsg = request.MessageText.ToLowerInvariant();
            bool isWakeWord = Array.Exists(WakeWords, word => lowerMsg.Contains(word));

            if (!isWakeWord)
            {
                _logger.LogInformation("Mensaje personal ignorado (Sin Wake-word). Cliente: {Phone}", request.CustomerPhone);
                return Result.Success();
            }
        }

        // BLINDAJE 3: Licencia Activa
        if (!await _entitlementService.IsLicenseValidAsync(request.WorkspaceId, cancellationToken))
        {
            _logger.LogWarning("Workspace {Workspace} inactivo o suspendido.", request.WorkspaceId);
            return Result.Success(); // No respondemos para no dar servicio gratis
        }

        // 1. IA: Analizar intención
        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);
        if (!intentResult.IsConfident()) intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        // 2. Despachador: Extraer datos reales
        var systemContext = await _moduleDispatcher.BuildSystemContextAsync(request.WorkspaceId, intentResult, cancellationToken);

        // 3. Redis: Mantener la sesión viva (Ej: Expira en 15 mins si no hablan)
        await _conversationCache.SetLastIntentAsync(request.WorkspaceId, request.CustomerPhone, intentResult.Intent.ToString(), cancellationToken);

        // 4. IA: Generar respuesta humana
        var finalResponse = await _aiRouter.GenerateResponseAsync(request.WorkspaceId, intentResult, systemContext, cancellationToken);

        // 5. WhatsApp: Enviar respuesta
        await _messageGateway.SendTextAsync(request.WorkspaceId, request.CustomerPhone, finalResponse, cancellationToken);

        return Result.Success();
    }
}