using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Application.DTOs.Conversations;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Knowledge;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

public class ProcessIncomingMessageCommandHandler
{
    private readonly IEntitlementService _entitlementService;
    private readonly IIntentEngine _intentEngine;
    private readonly IKnowledgeEngine _knowledgeEngine;
    private readonly IAiRouter _aiRouter;
    private readonly IConversationRepository _conversationRepository;
    private readonly IMessageGateway _messageGateway;
    private readonly IClock _clock;

    public ProcessIncomingMessageCommandHandler(
        IEntitlementService entitlementService,
        IIntentEngine intentEngine,
        IKnowledgeEngine knowledgeEngine,
        IAiRouter aiRouter,
        IConversationRepository conversationRepository,
        IMessageGateway messageGateway,
        IClock clock)
    {
        _entitlementService = entitlementService;
        _intentEngine = intentEngine;
        _knowledgeEngine = knowledgeEngine;
        _aiRouter = aiRouter;
        _conversationRepository = conversationRepository;
        _messageGateway = messageGateway;
        _clock = clock;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // 1. Guardián: ¿La licencia está activa?
        if (!await _entitlementService.IsLicenseValidAsync(request.WorkspaceId, cancellationToken))
        {
            // Ignoramos el mensaje silenciosamente si no pagan, o podríamos mandar un mensaje fijo
            return Result.Failure(new Error("License.Invalid", "El negocio no tiene licencia activa."));
        }

        // 2. Guardar mensaje entrante del usuario en la memoria
        var userMessage = new MessageDto("USER", request.Message, _clock.UtcNow);
        await _conversationRepository.SaveMessageAsync(request.WorkspaceId, request.CustomerIdentifier, userMessage, cancellationToken);

        // 3. Entender la intención
        var intentResult = await _intentEngine.AnalyzeAsync(request.Message, cancellationToken);

        // 4. Construir contexto (Software decide y busca)
        string systemContext = string.Empty;

        // Validamos si tienen el módulo FAQ activo
        bool hasFaqModule = await _entitlementService.HasModuleAccessAsync(request.WorkspaceId, "FAQ", cancellationToken);

        if (intentResult.Intent == "FAQ" && hasFaqModule)
        {
            var faqs = await _knowledgeEngine.SearchRelevantFaqsAsync(request.WorkspaceId, request.Message, cancellationToken);
            systemContext = $"FAQs Encontradas: {string.Join(" | ", faqs.Select(f => f.Question + ": " + f.Answer))}";
        }
        else if (intentResult.Intent == "CHECK_AVAILABILITY")
        {
            // Aquí llamaríamos al _reservationEngine.GetAvailabilityAsync...
            // Por brevedad en el diseño, imaginamos que nos devuelve esto:
            systemContext = "Horarios disponibles: 10:00 AM, 11:30 AM.";
        }

        // Si no detectó nada claro, le pasamos la info general del negocio
        if (string.IsNullOrEmpty(systemContext))
        {
            systemContext = await _knowledgeEngine.GetBusinessContextAsStringAsync(request.WorkspaceId, cancellationToken);
        }

        // 5. Generar la respuesta (IA redacta con empatía usando la data dura del software)
        var aiResponse = await _aiRouter.GenerateResponseAsync(
            request.WorkspaceId,
            intentResult,
            systemContext,
            cancellationToken);

        // 6. Enviar la respuesta vía WhatsApp (Evolution/n8n)
        await _messageGateway.SendTextAsync(request.WorkspaceId, request.CustomerIdentifier, aiResponse, cancellationToken);

        // 7. Guardar mensaje saliente del asistente
        var assistantMessage = new MessageDto("ASSISTANT", aiResponse, _clock.UtcNow);
        await _conversationRepository.SaveMessageAsync(request.WorkspaceId, request.CustomerIdentifier, assistantMessage, cancellationToken);

        return Result.Success();
    }
}