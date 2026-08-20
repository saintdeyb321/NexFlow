using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

// El "record" ya no está aquí, vive felizmente en su propio archivo.

public class ProcessIncomingMessageCommandHandler
{
    private readonly IIntentEngine _intentEngine;
    private readonly IAiRouter _aiRouter;
    private readonly IMessageGateway _messageGateway;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<ProcessIncomingMessageCommandHandler> _logger;

    public ProcessIncomingMessageCommandHandler(
        IIntentEngine intentEngine,
        IAiRouter aiRouter,
        IMessageGateway messageGateway,
        IEntitlementService entitlementService,
        ILogger<ProcessIncomingMessageCommandHandler> logger)
    {
        _intentEngine = intentEngine;
        _aiRouter = aiRouter;
        _messageGateway = messageGateway;
        _entitlementService = entitlementService;
        _logger = logger;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar que la licencia del negocio siga activa antes de gastar saldo de IA
        if (!await _entitlementService.IsLicenseValidAsync(request.WorkspaceId, cancellationToken))
        {
            _logger.LogWarning("Mensaje rechazado. Workspace {Workspace} sin licencia activa.", request.WorkspaceId);
            return Result.Failure(new Error("License.Invalid", "Licencia inactiva."));
        }

        // 2. Clasificar la intención
        var intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, cancellationToken);

        // 3. Evaluar Confianza (Fallback si la IA dudó)
        if (!intentResult.IsConfident())
        {
            intentResult = new IntentResultDto(IntentType.Unknown, 0, new());
        }

        // 4. Lógica de negocio (Mockeada por ahora hasta que conectemos el Firestore y la BD real en los siguientes pasos)
        string systemContext = intentResult.Intent switch
        {
            IntentType.Faq => "Respuestas de FAQ del negocio.",
            IntentType.CheckAvailability => "Horarios de hoy: 10:00, 15:00.",
            _ => "Contexto genérico del negocio."
        };

        // 5. Generar Respuesta amigable
        var finalResponse = await _aiRouter.GenerateResponseAsync(request.WorkspaceId, intentResult, systemContext, cancellationToken);

        // 6. Enviar mensaje de vuelta a WhatsApp
        await _messageGateway.SendTextAsync(request.WorkspaceId, request.CustomerPhone, finalResponse, cancellationToken);

        return Result.Success();
    }
}