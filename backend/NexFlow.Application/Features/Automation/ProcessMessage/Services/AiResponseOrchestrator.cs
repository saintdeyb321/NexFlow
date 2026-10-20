using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Domain.Enums;
using NexFlow.Application.Engines.AI;
using System.Text.RegularExpressions;
using NexFlow.Domain.Exceptions;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IAiResponseOrchestrator
{
    Task<MessageRecord> RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken);
}

public sealed class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly ITurnUnderstandingPipeline _understanding;
    private readonly IEntitlementService _entitlementService;
    private readonly IBookingFlow _bookingFlow;
    private readonly IRequestFlow _requestFlow;
    private readonly IOrderFlow _orderFlow;
    private readonly ISupportFlow _supportFlow;
    private readonly IChatFlow _chatFlow;
    private readonly IContextRecoveryService _contextRecovery;
    private readonly IOutboundMessageService _outboundMessageService;
    private readonly ILogger<AiResponseOrchestrator> _logger;
    private readonly IConversationRecoveryService? _recovery;
    private readonly IClock? _clock;

    public AiResponseOrchestrator(
        ITurnUnderstandingPipeline understanding, IEntitlementService entitlementService,
        IBookingFlow bookingFlow, IRequestFlow requestFlow, IOrderFlow orderFlow, ISupportFlow supportFlow, IChatFlow chatFlow,
        IContextRecoveryService contextRecovery, IOutboundMessageService outboundMessageService,
        ILogger<AiResponseOrchestrator> logger, IConversationRecoveryService? recovery = null, IClock? clock = null)
    {
        _understanding = understanding; _entitlementService = entitlementService;
        _bookingFlow = bookingFlow; _requestFlow = requestFlow; _orderFlow = orderFlow;
        _supportFlow = supportFlow; _chatFlow = chatFlow;
        _contextRecovery = contextRecovery; _outboundMessageService = outboundMessageService;
        _logger = logger;
        _recovery = recovery;
        _clock = clock;
    }

    public async Task<MessageRecord> RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        var context = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);
        if (context.Mode == "Human" || conversation.Mode == ConversationMode.Human)
            throw new ConcurrencyException("Conversation is under human control; reload its state before processing.");
        var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (activeModules.Contains("RESERVATIONS") && activeModules.Contains("SERVICES"))
        {
            var replay = await _bookingFlow.TryResumeAsync(workspaceId, normalizedPhone, request.MessageId, context, cancellationToken);
            if (replay != null)
                return await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, normalizedPhone,
                    replay, SenderType.AI, request.MessageId, cancellationToken);
        }

        var recovery = _recovery == null ? null : await _recovery.RecoverAsync(workspaceId, normalizedPhone, conversation.Id, request, context, activeModules, cancellationToken);
        if (recovery?.Response != null) return await SendText(recovery.Response);
        AiInterpretation interpretation;
        PreparedDialogueTurn? prepared = null;
        if (recovery?.Interpretation != null) interpretation = recovery.Interpretation;
        else
        {
            prepared = await _understanding.PrepareAsync(workspaceId, normalizedPhone, request, context, activeModules, cancellationToken);
            interpretation = prepared.Interpretation;
        }
        var normalized = ConversationRecoveryService.Normalize(request.MessageText);
        // A manual takeover while the provider was running invalidates this turn.
        var latest = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);
        if (latest.Mode == "Human" || latest.StateVersion != context.StateVersion)
            throw new ConcurrencyException("Conversation changed during interpretation; reload before processing.");
        activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (prepared?.Response != null)
            return await SendText(AiIntentAccess.GetDeniedResponse(interpretation.Intent, activeModules) ?? prepared.Response);
        interpretation.Confirmed = context.CurrentStep == "CONFIRM" && normalized is "si" or "si confirmo" or "confirmo" or "confirmar" or "de acuerdo";
        if (context.PendingRecoveryAction != null && interpretation.Intent != ConversationIntent.Support
            && (interpretation.Directive != ConversationDirective.None
                || interpretation.Intent is not (ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery or ConversationIntent.BusinessHours or ConversationIntent.Location or ConversationIntent.Faq or ConversationIntent.ProviderUnavailable or ConversationIntent.RateLimited)))
            return await SendText("Antes de continuar, confirma la decisión pendiente: responde sí o no, o escribe asesor para recibir ayuda.");

        if (context.CurrentGoal is "BOOKING" or "RESERVATION" && interpretation.Intent is ConversationIntent.Order or ConversationIntent.Request
            || context.CurrentGoal == "ORDER" && interpretation.Intent is ConversationIntent.Reservation or ConversationIntent.Request)
            return await SendText("Tu borrador sigue guardado. Completa el proceso actual o escribe cancelar para descartarlo con confirmación antes de iniciar otro.");

        if (interpretation.Directive == ConversationDirective.CancelCurrentFlow && _recovery != null)
        {
            var confirmation = await _recovery.RecoverAsync(workspaceId, normalizedPhone, conversation.Id, request with { MessageText = "cancelar" }, context, activeModules, cancellationToken);
            return await SendText(confirmation!.Response!);
        }
        if (_recovery != null && interpretation.Intent is not (ConversationIntent.ProviderUnavailable or ConversationIntent.RateLimited)
            && (interpretation.RequiresClarification || interpretation.Intent is ConversationIntent.General
            || recovery?.Interpretation == null && interpretation.BookingProposal == null && string.IsNullOrEmpty(context.CurrentGoal) && ((interpretation.Intent == ConversationIntent.Reservation && !Regex.IsMatch(normalized, @"\b(reserv|agend|cita)"))
                || (interpretation.Intent == ConversationIntent.Order && !Regex.IsMatch(normalized, @"\b(compr|pedid|pedir|llevar|orden)"))
                || (interpretation.Intent == ConversationIntent.Request && !Regex.IsMatch(normalized, @"\b(solicit|reclamo|queja|cotiz|registr|tramite|soporte)")))))
        {
            var introduction = interpretation.Intent == ConversationIntent.RateLimited ? "El proveedor de IA alcanzó su límite temporal. Puedes usar estas opciones sin IA:"
                : interpretation.Intent == ConversationIntent.ProviderUnavailable ? "No pude interpretar tu consulta en este momento. Podemos continuar con opciones verificadas:"
                : "Necesito precisar qué deseas hacer. Elige una opción o escribe el nombre completo de lo que necesitas:";
            var clarification = await _recovery.ClarifyAsync(workspaceId, normalizedPhone, request.MessageId, context, activeModules, introduction, cancellationToken);
            if (clarification.Response != null) return await SendText(clarification.Response);
            interpretation = clarification.Interpretation!;
        }

        string finalResponse;
        if (context.ClarificationAttempts > 0 && interpretation.Intent is not (ConversationIntent.General or ConversationIntent.ProviderUnavailable or ConversationIntent.RateLimited or ConversationIntent.Support))
        {
            context.ClarificationAttempts = 0;
            if (interpretation.BookingProposal == null) await _contextRecovery.SaveContextAsync(workspaceId, normalizedPhone, context, cancellationToken);
        }

        if (interpretation.Intent == ConversationIntent.ProviderUnavailable || interpretation.Intent == ConversationIntent.RateLimited)
        {
            finalResponse = interpretation.Intent == ConversationIntent.RateLimited ? "El proveedor de IA alcanzó su límite temporal. Puedes intentar en un minuto o solicitar un asesor."
                : "No pude interpretar tu consulta en este momento. Indica qué información necesitas o solicita un asesor.";
            if (context.CurrentGoal is "BOOKING" or "RESERVATION")
                finalResponse += "\n\n" + await _bookingFlow.GetContinuationAsync(workspaceId, context, cancellationToken, concise: true);
        }
        else
        {
            var effectiveIntent = context.CurrentGoal switch
            {
                "BOOKING" or "RESERVATION" => ConversationIntent.Reservation,
                "ORDER" => ConversationIntent.Order,
                _ => interpretation.Intent
            };
            var informational = interpretation.Directive == ConversationDirective.None && interpretation.Intent is ConversationIntent.ProductQuery
                or ConversationIntent.ServiceQuery or ConversationIntent.BusinessHours or ConversationIntent.Location or ConversationIntent.Faq;
            var activeFlowDenied = AiIntentAccess.GetDeniedResponse(effectiveIntent, activeModules);
            var deniedResponse = interpretation.Intent == ConversationIntent.Support ? null : interpretation.DeniedResponse
                ?? AiIntentAccess.GetDeniedResponse(interpretation.Intent, activeModules)
                ?? (informational ? null : activeFlowDenied);
            if (deniedResponse != null)
            {
                finalResponse = deniedResponse;
            }
            else if (interpretation.Intent == ConversationIntent.Support)
            {
                finalResponse = await _supportFlow.ProcessAsync(workspaceId, conversation.Id, cancellationToken);
            }
            else if (context.CurrentGoal is "ORDER" or "BOOKING" or "RESERVATION" && interpretation.Directive == ConversationDirective.None
                && interpretation.Intent is ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery or ConversationIntent.BusinessHours or ConversationIntent.Location or ConversationIntent.Faq)
            {
                var answer = await _chatFlow.ProcessAsync(workspaceId, request.MessageText, interpretation, context.SelectedLocationId, cancellationToken);
                var continuation = activeFlowDenied != null ? "Tu borrador sigue guardado. " + activeFlowDenied
                    : context.CurrentGoal == "ORDER" ? context.LastQuestion ?? "Tu pedido sigue guardado. Puedes mostrar pedido o continuar agregando productos."
                    : await _bookingFlow.GetContinuationAsync(workspaceId, context, cancellationToken, concise: true);
                if (context.PendingRecoveryAction is "CANCEL" or "EXPIRE") continuation = "Sigue pendiente tu confirmación para descartar el borrador: responde sí o no.";
                if (!string.IsNullOrWhiteSpace(continuation)) answer = answer with { Text = answer.Text + "\n\n" + continuation };
                return await SendChatResponseAsync(workspaceId, normalizedPhone, request.MessageId, conversation.Id, answer, cancellationToken);
            }
            else if ((context.CurrentGoal == "BOOKING" || context.CurrentGoal == "RESERVATION" || (string.IsNullOrWhiteSpace(context.CurrentGoal) && interpretation.Intent == ConversationIntent.Reservation)) && activeModules.Contains("RESERVATIONS"))
            {
                finalResponse = !activeModules.Contains("SERVICES")
                    ? AiIntentAccess.ServicesUnavailable
                    : await _bookingFlow.ProcessAsync(workspaceId, normalizedPhone, conversation.Id, context, interpretation, request.CustomerName, request.MessageId, cancellationToken);
                if (_recovery != null && context.CurrentGoal is "BOOKING" or "RESERVATION" && context.CurrentStep is "COLLECT_LOCATION" or "COLLECT_SERVICE" or "COLLECT_TIME")
                    finalResponse = await _recovery.DecorateAsync(workspaceId, normalizedPhone, context, activeModules, finalResponse, cancellationToken);
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
            else
            {
                var answer = await _chatFlow.ProcessAsync(workspaceId, request.MessageText, interpretation, null, cancellationToken);
                return await SendChatResponseAsync(workspaceId, normalizedPhone, request.MessageId, conversation.Id, answer, cancellationToken);
            }
        }

        if (interpretation.Intent is ConversationIntent.Support or ConversationIntent.Request && (await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken)).Mode == "Human")
            return await _outboundMessageService.SendHandoffAcknowledgementAsync(workspaceId, conversation.Id, normalizedPhone, finalResponse, request.MessageId, cancellationToken);
        return await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, normalizedPhone, finalResponse, SenderType.AI, request.MessageId, cancellationToken);
        Task<MessageRecord> SendText(string response) => _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, normalizedPhone, response, SenderType.AI, request.MessageId, cancellationToken);
    }

    private Task<MessageRecord> SendChatResponseAsync(Guid workspaceId, string phone, string sourceMessageId, string conversationId, ChatResponse response, CancellationToken ct)
        => response.MediaUrl != null
            ? _outboundMessageService.SendDocumentAsync(workspaceId, conversationId, phone, response.Text, response.MediaUrl,
                response.FileName!, response.CatalogScope!, sourceMessageId, ct)
            : _outboundMessageService.SendMessageAsync(workspaceId, conversationId, phone, response.Text, SenderType.AI, sourceMessageId, ct);
}
