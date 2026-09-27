using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IAiResponseOrchestrator
{
    // 🔥 SPRINT 1: Propagamos el estado real del envío al handler.
    Task<MessageRecord> RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken);
}

public sealed class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly IAiInterpreter _interpreter;
    private readonly IEntitlementService _entitlementService;
    private readonly IBookingFlow _bookingFlow;
    private readonly IRequestFlow _requestFlow;
    private readonly IOrderFlow _orderFlow; // 🔥 Inyección del flujo de Pedidos
    private readonly ISupportFlow _supportFlow;
    private readonly IChatFlow _chatFlow;
    private readonly IContextRecoveryService _contextRecovery;
    private readonly IConversationCache _cache; // 🔥 Para un guardado de estado totalmente limpio
    private readonly IOutboundMessageService _outboundMessageService;
    private readonly ILogger<AiResponseOrchestrator> _logger;

    public AiResponseOrchestrator(
        IAiInterpreter interpreter, IEntitlementService entitlementService,
        IBookingFlow bookingFlow, IRequestFlow requestFlow, IOrderFlow orderFlow, ISupportFlow supportFlow, IChatFlow chatFlow,
        IContextRecoveryService contextRecovery, IConversationCache cache, IOutboundMessageService outboundMessageService,
        ILogger<AiResponseOrchestrator> logger)
    {
        _interpreter = interpreter; _entitlementService = entitlementService;
        _bookingFlow = bookingFlow; _requestFlow = requestFlow; _orderFlow = orderFlow;
        _supportFlow = supportFlow; _chatFlow = chatFlow;
        _contextRecovery = contextRecovery; _cache = cache; _outboundMessageService = outboundMessageService;
        _logger = logger;
    }

    // 🔥 SPRINT 1: Devolvemos el resultado del servicio de salida.
    public async Task<MessageRecord> RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        var context = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);
        var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var interpretation = await _interpreter.InterpretAsync(workspaceId, request.MessageText, context.CurrentGoal ?? "", activeModules, cancellationToken);

        string finalResponse;

        // 🔥 SPRINT 3: La licencia se valida antes de despachar cualquier flujo con acceso a datos.
        var deniedResponse = OfferingQueryAccess.GetDeniedResponse(interpretation.Intent, activeModules);
        if (deniedResponse != null)
        {
            finalResponse = deniedResponse;
        }
        else if ((interpretation.Intent == "RESERVATION" || interpretation.Intent == "BOOKING" || context.CurrentGoal == "BOOKING" || context.CurrentGoal == "RESERVATION") && activeModules.Contains("RESERVATIONS"))
        {
            finalResponse = !activeModules.Contains("SERVICES")
                ? OfferingQueryAccess.ServicesUnavailable
                : await _bookingFlow.ProcessAsync(workspaceId, normalizedPhone, conversation.Id, context, interpretation, request.CustomerName, cancellationToken);
        }
        else if (interpretation.Intent == "ORDER" && activeModules.Contains("ORDERS"))
        {
            // 🔥 SPRINT 3: ORDERS no concede permiso para consultar productos de CATALOG.
            if (!activeModules.Contains("CATALOG"))
                finalResponse = OfferingQueryAccess.ProductsUnavailable;
            else
            {
                finalResponse = await _orderFlow.ProcessAsync(workspaceId, normalizedPhone, conversation.Id, request.CustomerName, interpretation, cancellationToken);
                context.CurrentGoal = null;
            }
        }
        else if (interpretation.Intent == "REQUEST" && activeModules.Contains("REQUESTS"))
        {
            finalResponse = await _requestFlow.ProcessAsync(workspaceId, normalizedPhone, request.MessageText, conversation.Id, cancellationToken);
        }
        // 🔥 AVISO: Tu intérprete no tiene la opción 'SUPPORT' en su lista de intenciones.
        // Si quieres que el bot pase a modo humano, debes agregar 'SUPPORT' a la lista del AiInterpreter.cs
        else if (interpretation.Intent == "SUPPORT")
        {
            finalResponse = await _supportFlow.ProcessAsync(workspaceId, conversation.Id, cancellationToken);
        }
        else
        {
            finalResponse = await _chatFlow.ProcessAsync(workspaceId, request.MessageText, interpretation, cancellationToken);
        }

        // 🔥 SPRINT 1: Persistimos el contexto actualizado antes de enviar la respuesta.
        await _cache.SetContextAsync(workspaceId, normalizedPhone, context, cancellationToken);
        return await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, normalizedPhone, finalResponse, SenderType.AI, cancellationToken);
    }
}
