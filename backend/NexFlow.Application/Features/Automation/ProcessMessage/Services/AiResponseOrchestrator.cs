using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IAiResponseOrchestrator
{
    Task<MessageRecord> RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken);
}

public sealed class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly IAiInterpreter _interpreter;
    private readonly IEntitlementService _entitlementService;
    private readonly IBookingFlow _bookingFlow;
    private readonly IRequestFlow _requestFlow;
    private readonly IOrderFlow _orderFlow;
    private readonly ISupportFlow _supportFlow;
    private readonly IChatFlow _chatFlow;
    private readonly IContextRecoveryService _contextRecovery;
    private readonly IOutboundMessageService _outboundMessageService;
    private readonly ILogger<AiResponseOrchestrator> _logger;

    public AiResponseOrchestrator(
        IAiInterpreter interpreter, IEntitlementService entitlementService,
        IBookingFlow bookingFlow, IRequestFlow requestFlow, IOrderFlow orderFlow, ISupportFlow supportFlow, IChatFlow chatFlow,
        IContextRecoveryService contextRecovery, IOutboundMessageService outboundMessageService,
        ILogger<AiResponseOrchestrator> logger)
    {
        _interpreter = interpreter; _entitlementService = entitlementService;
        _bookingFlow = bookingFlow; _requestFlow = requestFlow; _orderFlow = orderFlow;
        _supportFlow = supportFlow; _chatFlow = chatFlow;
        _contextRecovery = contextRecovery; _outboundMessageService = outboundMessageService;
        _logger = logger;
    }

    public async Task<MessageRecord> RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        var context = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);
        var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (activeModules.Contains("RESERVATIONS") && activeModules.Contains("SERVICES"))
        {
            var replay = await _bookingFlow.TryResumeAsync(workspaceId, normalizedPhone, request.MessageId, context, cancellationToken);
            if (replay != null)
                return await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, normalizedPhone,
                    replay, SenderType.AI, request.MessageId, cancellationToken);
        }

        var interpretation = await _interpreter.InterpretAsync(workspaceId, request.MessageText, context.CurrentGoal ?? "", context.CurrentStep, activeModules, cancellationToken);

        string finalResponse;

        if (interpretation.Intent == ConversationIntent.ProviderUnavailable || interpretation.Intent == ConversationIntent.RateLimited)
        {
            finalResponse = "Lo siento, en este momento estoy experimentando una alta demanda y no puedo procesar tu consulta. Por favor, intenta de nuevo en unos minutos.";
        }
        else
        {
            var effectiveIntent = context.CurrentGoal switch
            {
                "BOOKING" or "RESERVATION" => ConversationIntent.Reservation,
                "ORDER" => ConversationIntent.Order,
                _ => interpretation.Intent
            };
            var deniedResponse = interpretation.DeniedResponse
                ?? AiIntentAccess.GetDeniedResponse(interpretation.Intent, activeModules)
                ?? AiIntentAccess.GetDeniedResponse(effectiveIntent, activeModules);
            if (deniedResponse != null)
            {
                finalResponse = deniedResponse;
            }
            else if ((context.CurrentGoal == "ORDER" && (interpretation.Intent is ConversationIntent.BusinessHours or ConversationIntent.Location or ConversationIntent.Faq))
                || ((context.CurrentGoal is "BOOKING" or "RESERVATION") && interpretation.Directive == ConversationDirective.None
                    && (interpretation.Intent is ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery)))
            {
                var answer = await _chatFlow.ProcessAsync(workspaceId, request.MessageText, interpretation, context.SelectedLocationId, cancellationToken);
                var continuation = context.CurrentGoal == "ORDER" ? context.LastQuestion
                    : await _bookingFlow.GetContinuationAsync(workspaceId, context, cancellationToken, concise: true);
                if (!string.IsNullOrWhiteSpace(continuation)) answer = answer with { Text = answer.Text + "\n\n" + continuation };
                return await SendChatResponseAsync(workspaceId, normalizedPhone, request.MessageId, conversation.Id, answer, cancellationToken);
            }
            else if ((context.CurrentGoal == "BOOKING" || context.CurrentGoal == "RESERVATION" || (string.IsNullOrWhiteSpace(context.CurrentGoal) && interpretation.Intent == ConversationIntent.Reservation)) && activeModules.Contains("RESERVATIONS"))
            {
                finalResponse = !activeModules.Contains("SERVICES")
                    ? AiIntentAccess.ServicesUnavailable
                    : await _bookingFlow.ProcessAsync(workspaceId, normalizedPhone, conversation.Id, context, interpretation, request.CustomerName, request.MessageId, cancellationToken);
            }
            else if ((context.CurrentGoal == "ORDER" || (string.IsNullOrWhiteSpace(context.CurrentGoal) && interpretation.Intent == ConversationIntent.Order)) && activeModules.Contains("ORDERS"))
            {
                if (!activeModules.Contains("CATALOG"))
                    finalResponse = AiIntentAccess.ProductsUnavailable;
                else
                {
                    finalResponse = await _orderFlow.ProcessAsync(workspaceId, normalizedPhone, conversation.Id, request.CustomerName, request.MessageText, interpretation, request.MessageId, cancellationToken);
                }
            }
            else if (interpretation.Intent == ConversationIntent.Request && activeModules.Contains("REQUESTS"))
            {
                finalResponse = await _requestFlow.ProcessAsync(workspaceId, normalizedPhone, request.MessageText, conversation.Id, request.MessageId, cancellationToken);
            }
            else if (interpretation.Intent == ConversationIntent.Support)
            {
                finalResponse = await _supportFlow.ProcessAsync(workspaceId, conversation.Id, cancellationToken);
            }
            else
            {
                var answer = await _chatFlow.ProcessAsync(workspaceId, request.MessageText, interpretation, null, cancellationToken);
                return await SendChatResponseAsync(workspaceId, normalizedPhone, request.MessageId, conversation.Id, answer, cancellationToken);
            }
        }

        return await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, normalizedPhone, finalResponse, SenderType.AI, request.MessageId, cancellationToken);
    }

    private Task<MessageRecord> SendChatResponseAsync(Guid workspaceId, string phone, string sourceMessageId, string conversationId, ChatResponse response, CancellationToken ct)
        => response.MediaUrl != null
            ? _outboundMessageService.SendDocumentAsync(workspaceId, conversationId, phone, response.Text, response.MediaUrl,
                response.FileName!, response.CatalogScope!, sourceMessageId, ct)
            : _outboundMessageService.SendMessageAsync(workspaceId, conversationId, phone, response.Text, SenderType.AI, sourceMessageId, ct);
}
