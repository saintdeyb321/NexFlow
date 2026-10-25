using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Domain.Enums;
using NexFlow.Application.Engines.AI;
using NexFlow.Domain.Exceptions;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Composition;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.Dialogue.Policy;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IAiResponseOrchestrator
{
    Task<MessageRecord> RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken);
}

public sealed class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly ITurnUnderstandingPipeline _understanding;
    private readonly IResponseComposer _composer;
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
    private readonly IExistingReservationFlow? _existingReservations;

    public AiResponseOrchestrator(
        ITurnUnderstandingPipeline understanding, IEntitlementService entitlementService,
        IBookingFlow bookingFlow, IRequestFlow requestFlow, IOrderFlow orderFlow, ISupportFlow supportFlow, IChatFlow chatFlow,
        IContextRecoveryService contextRecovery, IOutboundMessageService outboundMessageService,
        ILogger<AiResponseOrchestrator> logger, IResponseComposer composer, IConversationRecoveryService? recovery = null, IClock? clock = null,
        IExistingReservationFlow? existingReservations = null)
    {
        _understanding = understanding; _entitlementService = entitlementService;
        _bookingFlow = bookingFlow; _requestFlow = requestFlow; _orderFlow = orderFlow;
        _supportFlow = supportFlow; _chatFlow = chatFlow;
        _contextRecovery = contextRecovery; _outboundMessageService = outboundMessageService;
        _logger = logger; _composer = composer;
        _recovery = recovery;
        _clock = clock;
        _existingReservations = existingReservations;
    }

    public Task<MessageRecord> RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
        => ConversationDiagnostics.MeasureAsync(_logger, "ResponseGeneration",
            () => RespondCoreAsync(workspaceId, normalizedPhone, request, conversation, cancellationToken), result => result.Status.ToString());

    private async Task<MessageRecord> RespondCoreAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        ResponsePlan? responsePlan = null;
        var intent = ConversationIntent.General;
        var act = DialogueAct.Information;
        var operation = OperationStatus.Informational;
        var expectedReply = ExpectedReply.Information;
        var context = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);
        if (context.Mode == "Human" || conversation.Mode == ConversationMode.Human)
            throw new ConcurrencyException("Conversation is under human control; reload its state before processing.");
        var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recoveredOrder = await _orderFlow.TryResumeAsync(workspaceId,normalizedPhone,conversation.Id,request.MessageId,context,cancellationToken);
        if (recoveredOrder != null)
            return await _outboundMessageService.SendMessageAsync(workspaceId,conversation.Id,normalizedPhone,recoveredOrder,SenderType.AI,request.MessageId,cancellationToken);

        if (activeModules.Contains("RESERVATIONS") && activeModules.Contains("SERVICES"))
        {
            var replay = await _bookingFlow.TryResumeAsync(workspaceId, normalizedPhone, request.MessageId, context, cancellationToken);
            if (replay != null)
                return await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, normalizedPhone,
                    replay, SenderType.AI, request.MessageId, cancellationToken);
        }

        if (_existingReservations != null)
        {
            var managed = await _existingReservations.TryHandleAsync(workspaceId, normalizedPhone, request, context, activeModules, cancellationToken);
            if (managed?.Operation == OperationStatus.Human)
            {
                await _supportFlow.ProcessAsync(workspaceId, conversation.Id, cancellationToken);
                return await _outboundMessageService.SendHandoffAcknowledgementAsync(workspaceId, conversation.Id, normalizedPhone,
                    managed.Response!, request.MessageId, cancellationToken);
            }
            if (managed?.Response != null) { intent = ConversationIntent.Reservation; return await SendText(managed.Response); }
        }
        var recovery = _recovery == null ? null : await _recovery.RecoverAsync(workspaceId, normalizedPhone, conversation.Id, request, context, activeModules, cancellationToken);
        if (recovery?.Response != null) { intent = recovery.Interpretation?.Intent ?? ConversationIntent.General; act = recovery.Act; operation = recovery.Operation; expectedReply = recovery.Expected; return await SendText(recovery.Response); }
        AiInterpretation interpretation;
        PreparedDialogueTurn? prepared = null;
        if (recovery?.Interpretation != null) interpretation = recovery.Interpretation;
        else
        {
            prepared = await _understanding.PrepareAsync(workspaceId, normalizedPhone, request, context, activeModules, cancellationToken);
            interpretation = prepared.Interpretation;
        }
        intent = interpretation.Intent;
        act = prepared?.Act ?? (interpretation.Intent == ConversationIntent.Reservation
            ? context.CurrentGoal is "BOOKING" or "RESERVATION" ? DialogueAct.RequestedData : DialogueAct.StartOperation
            : DialogueAct.Information);
        // A manual takeover while the provider was running invalidates this turn.
        var latest = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);
        if (latest.Mode == "Human" || latest.StateVersion != context.StateVersion)
            throw new ConcurrencyException("Conversation changed during interpretation; reload before processing.");
        activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (prepared?.Response != null)
        {
            var denied = AiIntentAccess.GetDeniedResponse(interpretation.Intent, activeModules);
            if (denied == null && prepared.Choices != null && _recovery != null)
            {
                var clarification = await _recovery.ClarifyAsync(workspaceId, normalizedPhone, request.MessageId, context,
                    activeModules, prepared.Response, cancellationToken, contextual: true, choices: prepared.Choices);
                expectedReply = clarification.Expected;
                return await SendText(clarification.Response!);
            }
            var response = denied ?? prepared.Response;
            if (denied == null && prepared.Act == DialogueAct.OperationInformation && context.CurrentGoal == null
                && interpretation.Intent == ConversationIntent.Reservation)
                expectedReply = ExpectedReply.StartReservation;
            if (denied == null && prepared.Act == DialogueAct.OperationInformation && context.CurrentGoal is "BOOKING" or "RESERVATION")
                response += "\n\n" + await _bookingFlow.GetContinuationAsync(workspaceId, context, cancellationToken, concise: true);
            return await SendText(response);
        }
        interpretation.Confirmed = ConversationQuestion.CanConfirmReservation(workspaceId, context, request.MessageText, _clock?.UtcNow ?? DateTime.UtcNow);
        if (interpretation.Confirmed) act = DialogueAct.Confirmation;
        else if (interpretation.Intent == ConversationIntent.Support) act = DialogueAct.HumanAssistance;
        else if (interpretation.Directive == ConversationDirective.CancelCurrentFlow) act = DialogueAct.Cancellation;
        if (context.PendingRecoveryAction != null && interpretation.Intent != ConversationIntent.Support
            && (interpretation.Directive != ConversationDirective.None
                || interpretation.Intent is not (ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery or ConversationIntent.BusinessHours or ConversationIntent.Location or ConversationIntent.Faq or ConversationIntent.ProviderUnavailable or ConversationIntent.RateLimited)))
            return await SendText("Antes de continuar, confirma la decisión pendiente: responde sí o no, o escribe asesor para recibir ayuda.");

        if (context.CurrentGoal is "BOOKING" or "RESERVATION" && interpretation.Intent is ConversationIntent.Order or ConversationIntent.Request
            || context.CurrentGoal == "ORDER" && interpretation.Intent is ConversationIntent.Reservation or ConversationIntent.Request)
            return await SendText("Para iniciar otra operación, primero completa la actual o escribe cancelar; te pediré confirmación antes de descartarla.");

        if (interpretation.Directive == ConversationDirective.CancelCurrentFlow && _recovery != null)
        {
            var confirmation = await _recovery.RecoverAsync(workspaceId, normalizedPhone, conversation.Id, request with { MessageText = "cancelar" }, context, activeModules, cancellationToken);
            return await SendText(confirmation!.Response!);
        }
        if (_recovery != null && interpretation.Intent is not (ConversationIntent.ProviderUnavailable or ConversationIntent.RateLimited)
            && (interpretation.RequiresClarification || interpretation.Intent is ConversationIntent.General
            || recovery?.Interpretation == null && interpretation.BookingProposal == null && interpretation.OrderProposal == null
                && string.IsNullOrEmpty(context.CurrentGoal) && interpretation.Intent is ConversationIntent.Reservation or ConversationIntent.Order or ConversationIntent.Request
                && prepared?.Act != DialogueAct.StartOperation))
        {
            if (context.CurrentGoal is "BOOKING" or "RESERVATION")
                return await SendText(AiIntentAccess.GetDeniedResponse(ConversationIntent.Reservation, activeModules)
                    ?? await _bookingFlow.GetContinuationAsync(workspaceId, context, cancellationToken, concise: true));
            var introduction = interpretation.Intent == ConversationIntent.RateLimited ? "El proveedor de IA alcanzó su límite temporal. Puedes usar estas opciones sin IA:"
                : interpretation.Intent == ConversationIntent.ProviderUnavailable ? "No pude interpretar tu consulta en este momento. Podemos continuar con opciones verificadas:"
                : "¿Qué información necesitas consultar o qué operación deseas realizar?";
            var clarification = await _recovery.ClarifyAsync(workspaceId, normalizedPhone, request.MessageId, context, activeModules, introduction, cancellationToken, contextual: true);
            if (clarification.Response != null) return await SendText(clarification.Response);
            interpretation = clarification.Interpretation!;
        }

        string finalResponse;
        if (context.ClarificationAttempts > 0 && interpretation.Intent is not (ConversationIntent.General or ConversationIntent.ProviderUnavailable or ConversationIntent.RateLimited or ConversationIntent.Support))
        {
            context.ClarificationAttempts = 0;
            if (interpretation.BookingProposal == null && interpretation.OrderProposal == null && interpretation.OfferingQuery == null) await _contextRecovery.SaveContextAsync(workspaceId, normalizedPhone, context, cancellationToken);
        }

        if (interpretation.Intent == ConversationIntent.ProviderUnavailable || interpretation.Intent == ConversationIntent.RateLimited)
        {
            finalResponse = interpretation.Intent == ConversationIntent.RateLimited ? "Hemos alcanzado el límite temporal de consultas. Puedes intentar en un minuto o solicitar un asesor."
                : "Podemos continuar con una consulta concreta o solicitar un asesor. ¿Qué información necesitas?";
            if (context.CurrentGoal is "BOOKING" or "RESERVATION")
                finalResponse = AiIntentAccess.GetDeniedResponse(ConversationIntent.Reservation, activeModules)
                    ?? await _bookingFlow.GetContinuationAsync(workspaceId, context, cancellationToken, concise: true);
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
                operation = OperationStatus.Denied;
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
                var continuation = activeFlowDenied != null ? "Conservamos lo que has indicado. " + activeFlowDenied
                    : context.CurrentGoal == "ORDER" ? context.LastQuestion ?? "Tu pedido sigue guardado. Puedes mostrar pedido o continuar agregando productos."
                    : await _bookingFlow.GetContinuationAsync(workspaceId, context, cancellationToken, concise: true);
                if (context.PendingRecoveryAction is "CANCEL" or "EXPIRE") continuation = context.CurrentGoal is "BOOKING" or "RESERVATION"
                    ? "¿Deseas dejar esta reserva en preparación? Responde sí o no."
                    : "Sigue pendiente tu confirmación para descartar el borrador: responde sí o no.";
                if (interpretation.OfferingQuery?.Clarification != null && context.CurrentGoal == "ORDER" && activeFlowDenied == null)
                    continuation = "Conservamos tu pedido mientras aclaramos esta consulta.";
                if (!string.IsNullOrWhiteSpace(continuation)) answer = answer with { Text = answer.Text + "\n\n" + continuation };
                await RememberOfferingQuery();
                return await SendChat(answer);
            }
            else if ((context.CurrentGoal == "BOOKING" || context.CurrentGoal == "RESERVATION" || (string.IsNullOrWhiteSpace(context.CurrentGoal) && interpretation.Intent == ConversationIntent.Reservation)) && activeModules.Contains("RESERVATIONS"))
            {
                if (!activeModules.Contains("SERVICES")) finalResponse = AiIntentAccess.ServicesUnavailable;
                else
                {
                    responsePlan = await _bookingFlow.ProcessPlannedAsync(workspaceId, normalizedPhone, conversation.Id, context, interpretation, request.CustomerName, request.MessageId, act, cancellationToken);
                    finalResponse = responsePlan.FallbackText;
                }
                if (_recovery != null && interpretation.BookingProposal?.ReadOnly != true && context.CurrentGoal is "BOOKING" or "RESERVATION" && context.CurrentStep is "COLLECT_LOCATION" or "COLLECT_SERVICE" or "COLLECT_TIME")
                {
                    finalResponse = await _recovery.DecorateAsync(workspaceId, normalizedPhone, context, activeModules, finalResponse, cancellationToken,
                        context.CurrentStep == "COLLECT_TIME" && responsePlan?.Choices.Count > 0 ? responsePlan.Choices : null);
                    responsePlan = responsePlan?.WithText(finalResponse, context.StateVersion, context.RecoveryOptions);
                }
            }
            else if ((context.CurrentGoal == "ORDER" || (string.IsNullOrWhiteSpace(context.CurrentGoal) && interpretation.Intent == ConversationIntent.Order)) && activeModules.Contains("ORDERS"))
            {
                if (!activeModules.Contains("CATALOG"))
                    finalResponse = AiIntentAccess.ProductsUnavailable;
                else
                {
                    responsePlan = await _orderFlow.ProcessPlannedAsync(workspaceId, normalizedPhone, conversation.Id, request.CustomerName, request.MessageText, interpretation, request.MessageId, act, cancellationToken);
                    finalResponse = responsePlan.FallbackText;
                    context = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);
                    if (context.StateVersion != responsePlan.StateVersion) throw new ConcurrencyException("Order changed after its result was prepared.");
                }
            }
            else if (interpretation.Intent == ConversationIntent.Request && activeModules.Contains("REQUESTS"))
            {
                finalResponse = await _requestFlow.ProcessAsync(workspaceId, normalizedPhone, request.MessageText, conversation.Id, request.MessageId, cancellationToken);
            }
            else
            {
                var answer = await _chatFlow.ProcessAsync(workspaceId, request.MessageText, interpretation, null, cancellationToken);
                await RememberOfferingQuery();
                return await SendChat(answer);
            }
        }

        if (interpretation.Intent is ConversationIntent.Support or ConversationIntent.Request && (await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken)).Mode == "Human")
            return await _outboundMessageService.SendHandoffAcknowledgementAsync(workspaceId, conversation.Id, normalizedPhone, finalResponse, request.MessageId, cancellationToken);
        return await SendText(finalResponse);

        async Task RememberOfferingQuery()
        {
            if (interpretation.OfferingQuery is not { } query) return;
            if (query.WorkspaceId != workspaceId || query.SourceMessageId != request.MessageId || query.StateVersion != context.StateVersion)
                throw new ConcurrencyException("Offering query no longer matches this turn.");
            if (query.Offerings.Count > 0) new OfferingReference(workspaceId,query.Type,query.Offerings.Select(o => o.Id).ToArray(),query.LocationId).Remember(context);
            else context.LastIntent = interpretation.Intent.ToString();
            InformationalRequest.Remember(context, query.Type == "PRODUCT" ? NexFlow.Application.Features.Knowledge.KnowledgeTopic.Products : NexFlow.Application.Features.Knowledge.KnowledgeTopic.Services,
                new() { LocationId = query.LocationId }, _clock?.UtcNow ?? DateTime.UtcNow);
            await _contextRecovery.SaveContextAsync(workspaceId,normalizedPhone,context,cancellationToken);
        }
        ResponsePlan Bind(string text, bool media = false) => responsePlan?.WithText(text, context.StateVersion)
            ?? new(workspaceId, request.MessageId, context.StateVersion, intent, act,
                media ? OperationStatus.MediaPending : operation, [], context.RecoveryOptions, [], null, null, null, text,
                !media && intent is ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery or ConversationIntent.Faq or ConversationIntent.Location or ConversationIntent.BusinessHours);

        async Task<string> Compose(string text, bool media = false)
        {
            var expected = context.PendingRecoveryAction != null ? ExpectedReply.RecoveryDecision
                : responsePlan?.Operation == OperationStatus.AwaitingConfirmation && intent == ConversationIntent.Reservation ? ExpectedReply.ConfirmReservation
                : responsePlan?.Operation == OperationStatus.AwaitingConfirmation && intent == ConversationIntent.Order ? ExpectedReply.ConfirmOrder
                : responsePlan?.Operation == OperationStatus.Collecting ? context.RecoveryOptions.Count > 0 ? ExpectedReply.Selection : ExpectedReply.RequestedData
                : expectedReply;
            ConversationQuestion.Remember(workspaceId, request.MessageId, context, expected, intent, text, _clock?.UtcNow ?? DateTime.UtcNow);
            await _contextRecovery.SaveContextAsync(workspaceId, normalizedPhone, context, cancellationToken);
            var plan = Bind(text, media);
            try { _logger.LogInformation("DialogueDecision Intent={Intent} Act={Act} Operation={Operation}", plan.Intent, plan.Act, plan.Operation); }
            catch { /* Diagnostics cannot change the business decision. */ }
            if (plan.WorkspaceId != workspaceId || plan.SourceMessageId != request.MessageId)
                throw new ConcurrencyException("Response identity mismatch.");
            var result = await ConversationDiagnostics.MeasureAsync(_logger, "Composition",
                () => _composer.ComposeAsync(plan, normalizedPhone, cancellationToken), result => result.Outcome);
            var current = await _contextRecovery.GetOrRecoverContextAsync(workspaceId, normalizedPhone, cancellationToken);
            if (current.Mode == "Human" || current.StateVersion != plan.StateVersion)
                throw new ConcurrencyException("Conversation changed during composition; reload before delivery.");
            var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return AiIntentAccess.GetDeniedResponse(plan.Intent, modules) ?? result.Text;
        }
        async Task<MessageRecord> SendText(string response) => await _outboundMessageService.SendMessageAsync(workspaceId,
            conversation.Id, normalizedPhone, await Compose(response), SenderType.AI, request.MessageId, cancellationToken);
        async Task<MessageRecord> SendChat(ChatResponse response)
        {
            if (context.ReservationManagement != null) response = response with { Text = response.Text + "\n\n" + context.ReservationManagement.NextQuestion };
            if (response.Information != null)
            {
                InformationalRequest.Remember(context, response.Information.Source, response.Information, _clock?.UtcNow ?? DateTime.UtcNow);
                await _contextRecovery.SaveContextAsync(workspaceId, normalizedPhone, context, cancellationToken);
            }
            if (response.Choices != null && _recovery != null)
            {
                var currentModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var clarification = await _recovery.ClarifyAsync(workspaceId, normalizedPhone, request.MessageId, context,
                    currentModules, response.Text, cancellationToken, contextual: true, choices: response.Choices);
                expectedReply = clarification.Expected;
                response = response with { Text = clarification.Response! };
            }
            responsePlan = new(workspaceId, request.MessageId, context.StateVersion, intent, act,
                response.MediaUrl != null ? OperationStatus.MediaPending : response.Operation, response.Facts, response.Choices == null ? [] : context.RecoveryOptions, [], null, null, null, response.Text,
                response.MediaUrl == null && response.Operation == OperationStatus.Informational);
            var text = await Compose(response.Text, response.MediaUrl != null);
            // A module can be revoked during optional wording. Never send restricted media afterwards.
            var modules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return AiIntentAccess.GetDeniedResponse(intent, modules) is { } denied
                ? await _outboundMessageService.SendMessageAsync(workspaceId, conversation.Id, normalizedPhone, denied, SenderType.AI, request.MessageId, cancellationToken)
                : await SendChatResponseAsync(workspaceId, normalizedPhone, request.MessageId, conversation.Id, response with { Text = text }, cancellationToken);
        }
    }

    private Task<MessageRecord> SendChatResponseAsync(Guid workspaceId, string phone, string sourceMessageId, string conversationId, ChatResponse response, CancellationToken ct)
        => response.MediaUrl != null
            ? _outboundMessageService.SendDocumentAsync(workspaceId, conversationId, phone, response.Text, response.MediaUrl,
                response.FileName!, response.CatalogScope!, sourceMessageId, ct)
            : _outboundMessageService.SendMessageAsync(workspaceId, conversationId, phone, response.Text, SenderType.AI, sourceMessageId, ct);
}
