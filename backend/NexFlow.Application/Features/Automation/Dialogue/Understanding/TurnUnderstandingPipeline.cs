using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.Business.Offerings;
using Microsoft.Extensions.Logging;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

public interface ITurnUnderstandingPipeline
{
    Task<PreparedDialogueTurn> PrepareAsync(Guid workspace, string phone, ProcessIncomingMessageCommand request,
        ConversationContextDto context, HashSet<string> modules, CancellationToken ct);
}

public sealed class TurnUnderstandingPipeline(IWorkspaceSlotResolver slots, DialoguePolicy policy,
    IAiInterpreter interpreter, IConversationRecoveryService recovery, IOfferingService offerings, ICommerceTurnResolver? commerce = null,
    ILogger<TurnUnderstandingPipeline>? logger = null, ILocationRepository? locationRepository = null) : ITurnUnderstandingPipeline
{
    public Task<PreparedDialogueTurn> PrepareAsync(Guid workspace, string phone, ProcessIncomingMessageCommand request,
        ConversationContextDto context, HashSet<string> modules, CancellationToken ct)
        => ConversationDiagnostics.MeasureAsync(logger, "Interpretation",
            () => PrepareWithRecoveryAsync(workspace, phone, request, context, modules, ct),
            result => { ConversationDiagnostics.Count("UnderstandingRoute", result.UsedAi ? "Semantic" : "Local"); return result.Act.ToString(); });

    private async Task<PreparedDialogueTurn> PrepareWithRecoveryAsync(Guid workspace, string phone, ProcessIncomingMessageCommand request,
        ConversationContextDto context, HashSet<string> modules, CancellationToken ct)
    {
        try { return await PrepareCoreAsync(workspace, phone, request, context, modules, ct); }
        catch (BusinessProfileUnavailableException) when (!ct.IsCancellationRequested)
        {
            return new(DialogueAct.BusinessUnavailable, new() { Intent = ConversationIntent.ProviderUnavailable }, Response:
                "No pude consultar los datos del negocio en este momento. Conservamos lo que has indicado. Inténtalo nuevamente en un momento o escribe asesor.");
        }
    }

    private async Task<PreparedDialogueTurn> PrepareCoreAsync(Guid workspace, string phone, ProcessIncomingMessageCommand request,
        ConversationContextDto context, HashSet<string> modules, CancellationToken ct)
    {
        var greeting = GreetingRecognizer.Parse(request.MessageText);
        if (greeting.HasGreeting && greeting.Content.Length > 0)
            request = request with { MessageText = greeting.Content };
        var act = policy.Classify(request.MessageText, context);
        if (act == DialogueAct.HumanAssistance) return new(act, new() { Intent = ConversationIntent.Support });
        if (context.CurrentGoal == "ORDER" && (context.CurrentStep?.StartsWith("ORDER_CONFIRM:", StringComparison.Ordinal) == true
            || context.CurrentStep?.StartsWith("ORDER_REVIEW:", StringComparison.Ordinal) == true)
            && ConversationConfirmation.IsAffirmative(request.MessageText))
            return new(DialogueAct.Confirmation, new() { Intent = ConversationIntent.Order,
                OrderProposal = new(workspace, request.MessageId, context.StateVersion, OrderTurnKind.Submit,
                    Array.AsReadOnly(Array.Empty<OrderEdit>())) });
        if (act == DialogueAct.Cancellation || act == DialogueAct.DeclineOperation && context.CurrentGoal != null)
            return new(DialogueAct.Cancellation, new() { Intent = ConversationIntent.General, Directive = ConversationDirective.CancelCurrentFlow });
        if (act == DialogueAct.DeclineOperation)
            return new(act, new() { Intent = ConversationIntent.General }, Response: "De acuerdo. ¿Qué información necesitas consultar?");
        if (act == DialogueAct.Resume)
        {
            if (context.Information != null) context.Information.Clarification = null;
            return new(act, new() { Intent = context.CurrentGoal == "ORDER" ? ConversationIntent.Order : ConversationIntent.Reservation });
        }
        var normalized = SpanishTextNormalizer.Normalize(request.MessageText);
        if (DialoguePolicy.IsInformationOnly(normalized))
            return new(DialogueAct.Information, new() { Intent = ConversationIntent.General }, Response: "De acuerdo, no iniciaré ninguna operación. ¿Qué información necesitas consultar?");
        var contradictory = DialoguePolicy.HasContradictoryGoals(normalized);
        if (context.CurrentGoal is "BOOKING" or "RESERVATION" && InformationalRequest.DraftServicePrice(request.MessageText))
        {
            if (!modules.Contains("SERVICES")) return new(DialogueAct.InformationalInterruption, new() { Intent = ConversationIntent.ServiceQuery }, Response: AiIntentAccess.ServicesUnavailable);
            var id = context.SelectedServiceId ?? (context.PendingServiceIds.Count == 1 ? context.PendingServiceIds[0] : null);
            var selected = id == null ? null : await offerings.GetServiceByIdAsync(workspace, id, ct);
            if (selected is not { IsActive: true, Type: "SERVICE" })
                return new(DialogueAct.InformationalInterruption, new() { Intent = ConversationIntent.ServiceQuery }, Response: "¿Sobre qué servicio deseas consultar el precio?");
            // Keep older constructor callers on the existing knowledge path.
            if (locationRepository == null)
                return new(DialogueAct.InformationalInterruption, new() { Intent = ConversationIntent.ServiceQuery, SearchTerm = selected.Name });
            // Reuse only this turn's current point read. ChatFlow still rechecks the
            // license; composition still validates StateVersion and human takeover.
            var verified = VerifiedOffering.Capture(selected);
            if (context.SelectedLocationId is { } locationId && (!verified.AtLocation(locationId)
                || !(await locationRepository.GetLocationsAsync(workspace, ct)).Any(l => l.Id == locationId)))
                return new(DialogueAct.InformationalInterruption, new() { Intent = ConversationIntent.ServiceQuery }, Response: "El servicio ya no está disponible en la sede indicada. ¿Sobre qué servicio deseas consultar el precio?");
            return new(DialogueAct.InformationalInterruption, new() {
                Intent = ConversationIntent.ServiceQuery, QueryKind = OfferingQueryKind.Specific,
                OfferingQuery = new(workspace, request.MessageId, context.StateVersion, "SERVICE", OfferingQuestion.Price,
                    Array.AsReadOnly(new[] { verified }), false, context.SelectedLocationId)
            });
        }
        if (act == DialogueAct.OperationInformation)
            return new(act, new() { Intent = ConversationIntent.Reservation }, Response:
                AiIntentAccess.GetDeniedResponse(ConversationIntent.Reservation, modules)
                    ?? (context.CurrentGoal is "BOOKING" or "RESERVATION" ? DialoguePolicy.ReservationProcess : DialoguePolicy.Instruction));
        var readOnlyQuestion = context.CurrentGoal is "BOOKING" or "RESERVATION"
            && (OrderMentionResolver.IsQuestion(request.MessageText) || OrderMentionResolver.IsHypothetical(request.MessageText));
        var temporalProbe = readOnlyQuestion && new TimePreferenceResolver().Resolve(request.MessageText).Status != ResolutionStatus.Insufficient;
        var activeBooking = context.CurrentGoal is "BOOKING" or "RESERVATION";
        if (!temporalProbe && (!activeBooking || readOnlyQuestion || InformationalRequest.Catalog(request.MessageText))
            && commerce != null && await commerce.ResolveAsync(workspace, request.MessageId, request.MessageText, context, modules, ct) is { } commercial)
            return commercial;
        var booking = context.CurrentGoal is "BOOKING" or "RESERVATION" || act == DialogueAct.StartOperation;
        var reservationCandidate = !booking && context.CurrentGoal == null && modules.Contains("RESERVATIONS") && modules.Contains("SERVICES")
            && act == DialogueAct.SemanticAmbiguity && !contradictory && !normalized.Split(' ').Contains("no")
            && normalized.Split(' ').Any(w => w is "quiero" or "quisiera" or "necesito" or "deseo");
        var purpose = booking || reservationCandidate ? SlotResolutionPurpose.Reservation : context.CurrentGoal == "ORDER" ? SlotResolutionPurpose.Order : SlotResolutionPurpose.Information;
        var local = await slots.ResolveAsync(workspace, request.MessageId, request.MessageText, context, purpose, policy.Reads(request.MessageText, context, act), ct);
        if (local.Time.Status == ResolutionStatus.Ambiguous && context.PreferredTime is { } preference)
        {
            var matching = local.Time.Candidates.Where(c => c.Value.ExactTime is { } time && TimePreferenceResolver.Matches(time, preference)).ToArray();
            if (matching.Length == 1) local = local with { Time = SlotResolution<TimePreference>.Match(matching[0].Value, matching[0].RawText) };
        }
        if (readOnlyQuestion && (local.Time.Status != ResolutionStatus.Insufficient || local.Date.Status != ResolutionStatus.Insufficient)
            && !new[] { local.Date.Code, local.Time.Code, local.Location.Code, local.Service.Code }.Any(code => code is "SemanticComposition" or "UnknownEntity"))
            return PrepareBooking(local, DialogueAct.InformationalInterruption, false, workspace, request.MessageId, readOnly: true);
        // An explicit literal choice of the just-verified time selects that whole
        // date/time reference. A question or acknowledgement never takes this path.
        if (booking && !readOnlyQuestion && TimePreferenceResolver.IsOnlyTimeExpression(request.MessageText)
            && local.Date.Status == ResolutionStatus.Insufficient && local.Time.TryGetValue(out var literalTime)
            && literalTime.Kind == TimePreferenceKind.Exact
            && ReservationTimeReference.Current(workspace, context, local.Context.LocalBusinessNow.UtcDateTime) is { } currentTime
            && literalTime.ExactTime!.Value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) == currentTime.Time)
            return SelectReference(currentTime, false);
        if (reservationCandidate && act == DialogueAct.SemanticAmbiguity && local.Service.Status == ResolutionStatus.Determined)
        { booking = true; act = DialogueAct.StartOperation; }
        if (context.PendingCustomerName != null && local.Name.Code == "SurnameMissing" && context.CurrentStep == "COLLECT_CUSTOMERNAME")
            local = local with { Name = new NameSlotResolver().Resolve(context.PendingCustomerName + " " + local.Name.Candidates[0].Value, true) };
        var dataTurn = booking && act is DialogueAct.StartOperation or DialogueAct.RequestedData or DialogueAct.Correction;
        if (booking && act == DialogueAct.Correction && ConversationConfirmation.IsCorrection(request.MessageText))
        {
            var words = normalized.Split(' ');
            if (local.Time.Status == ResolutionStatus.Insufficient && words.Any(w => w is "hora" or "horario"))
                local = local with { Time = SlotResolution<TimePreference>.Invalid("NewTimeRequired") };
            if (local.Date.Status == ResolutionStatus.Insufficient && words.Any(w => w is "dia" or "fecha"))
                local = local with { Date = SlotResolution<DateOnly>.Invalid("NewDateRequired") };
        }
        var understood = local.Name.Status == ResolutionStatus.Determined || local.Location.Status == ResolutionStatus.Determined
            || local.Service.Status == ResolutionStatus.Determined || local.Date.Status == ResolutionStatus.Determined || local.Time.Status == ResolutionStatus.Determined
            || local.Location.Status == ResolutionStatus.Ambiguous || local.Service.Status == ResolutionStatus.Ambiguous
            || local.Date.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid || local.Time.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid
            || local.Name.Candidates.Count > 0;
        var semanticGap = new[] { local.Date.Code, local.Time.Code, local.Name.Code, local.Location.Code, local.Service.Code }
            .Any(code => code is "SemanticComposition" or "UnknownEntity") || !understood && local.Time.Code == "TimeNotUnderstood";
        semanticGap |= booking && (local.Service.Status == ResolutionStatus.Ambiguous
                && local.Service.Candidates.Any(c => c.Value.Id == context.SelectedServiceId)
            || local.Location.Status == ResolutionStatus.Ambiguous
                && local.Location.Candidates.Any(c => c.Value.Id == context.SelectedLocationId)
            || local.Date.Status == ResolutionStatus.Ambiguous && context.TargetDate != null
            || local.Time.Status == ResolutionStatus.Ambiguous && context.TargetTime != null);
        // A partially understood compound answer can contain an unrecognized date
        // even when a name and another slot were parsed. Give the existing semantic
        // interpreter one pass rather than silently discarding the remainder.
        semanticGap |= local.Name.Status == ResolutionStatus.Determined && local.Date.Status == ResolutionStatus.Insufficient
            && (local.Time.Status == ResolutionStatus.Determined || local.Service.Status == ResolutionStatus.Determined);
        if (dataTurn && !semanticGap && (understood || act == DialogueAct.StartOperation))
            return PrepareBooking(local, act, false, workspace, request.MessageId);
        if (!await recovery.AcquireAiAsync(workspace, phone, request.MessageId, ct))
        {
            if (booking) return new(DialogueAct.ProviderFailure, new() { Intent = ConversationIntent.RateLimited });
            return new(DialogueAct.ProviderFailure, new() { Intent = ConversationIntent.RateLimited }, Response:
                "Hemos alcanzado el límite temporal de consultas. Espera un minuto o escribe asesor para recibir ayuda humana.");
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        AiInterpretation interpretation;
        try
        {
            var pending = interpreter is IContextualAiInterpreter contextual
                ? contextual.InterpretAsync(request.MessageText, local.Context, deadline.Token)
                : interpreter.InterpretAsync(workspace, request.MessageText, context.CurrentGoal ?? "", context.CurrentStep, modules, deadline.Token);
            interpretation = await pending.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { interpretation = new() { Intent = ConversationIntent.ProviderUnavailable, Failure = NexFlow.Application.Engines.AI.AiFailureKind.Timeout }; }
        if (interpretation.Intent is ConversationIntent.ProviderUnavailable or ConversationIntent.RateLimited)
            return new(DialogueAct.ProviderFailure, interpretation, true);
        if (commerce != null && interpretation.Intent is ConversationIntent.Order or ConversationIntent.ProductQuery or ConversationIntent.ServiceQuery
            && await commerce.ResolveAsync(workspace, request.MessageId, request.MessageText, context, modules, ct, interpretation) is { } understoodOrder)
            return understoodOrder with { UsedAi = true };
        var actEvidence = (interpretation.Evidence ?? []).Where(e => e != null && e.Slot == "Act").ToArray();
        var backedAct = !interpretation.RequiresClarification && actEvidence.Length == 1
            && !string.IsNullOrWhiteSpace(actEvidence[0].Text) && actEvidence[0].Text.Length <= 300
            && AiEvidenceValidator.HasFragment(request.MessageText, actEvidence[0].Text)
            && actEvidence[0].NormalizedValue == interpretation.Act?.ToString();
        if (interpretation.Directive == ConversationDirective.SelectReferencedTime)
        {
            var reference = ReservationTimeReference.Current(workspace, context, local.Context.LocalBusinessNow.UtcDateTime);
            if (!booking || interpretation.Intent != ConversationIntent.Reservation || !backedAct || interpretation.Act != DialogueAct.Correction
                || OrderMentionResolver.IsQuestion(request.MessageText) || OrderMentionResolver.IsHypothetical(request.MessageText) || reference == null)
                return new(DialogueAct.SemanticAmbiguity, new() { Intent = ConversationIntent.Reservation }, true,
                    "Indica la hora que deseas seleccionar; no hay una referencia inequívoca vigente para esa petición. Conservamos tu borrador.");
            return SelectReference(reference, true);
        }
        // Load only missing authorized candidates once when semantic understanding
        // identifies an entity that a simple-slot read deliberately omitted.
        if (booking && interpretation.Intent == ConversationIntent.Reservation
            && (interpretation.Service != null && local.AuthorizedServices.Count == 0 || interpretation.Location != null && local.AuthorizedLocations.Count == 0))
            local = await slots.ResolveAsync(workspace, request.MessageId, request.MessageText, context, SlotResolutionPurpose.Reservation, new(true, true, false), ct);
        if (booking && backedAct && interpretation.Act == DialogueAct.InformationalInterruption && interpretation.Intent == ConversationIntent.Reservation)
            return PrepareBooking(AiEvidenceValidator.Validate(local, interpretation, request.MessageText), DialogueAct.InformationalInterruption, true, workspace, request.MessageId, readOnly: true);
        if (booking && backedAct && interpretation.Act == DialogueAct.Correction
            && interpretation.Intent == ConversationIntent.Reservation)
            return PrepareBooking(AiEvidenceValidator.Validate(local, interpretation, request.MessageText), DialogueAct.Correction, true, workspace, request.MessageId);
        if (interpretation.Intent == ConversationIntent.Reservation && backedAct && act == DialogueAct.SemanticAmbiguity && context.CurrentGoal == null
            && !contradictory && !normalized.Split(' ').Contains("no") && interpretation.Act == DialogueAct.StartOperation)
        {
            // Semantic intent can start a draft; only licensed, reservable entities
            // from this workspace can enter it. Confirmation remains server-owned.
            local = await slots.ResolveAsync(workspace, request.MessageId, request.MessageText, context, SlotResolutionPurpose.Reservation, new(true, true, false), ct);
            local = AiEvidenceValidator.Validate(local, interpretation, request.MessageText);
            if (local.Service.Status == ResolutionStatus.Determined)
                return PrepareBooking(local, DialogueAct.StartOperation, true, workspace, request.MessageId);
        }
        // Informational interruptions never turn extracted entities into mutations of a business draft.
        if (interpretation.Directive == ConversationDirective.None && interpretation.Intent is ConversationIntent.ProductQuery
            or ConversationIntent.ServiceQuery or ConversationIntent.Location or ConversationIntent.BusinessHours or ConversationIntent.Faq or ConversationIntent.Profile)
            return new(booking ? DialogueAct.InformationalInterruption : DialogueAct.Information, interpretation, true);
        if (act == DialogueAct.SemanticAmbiguity && (contradictory || normalized.Split(' ').Contains("no")))
            return new(act, new() { Intent = ConversationIntent.General, RequiresClarification = true }, true);
        if (interpretation.Intent == ConversationIntent.Support) return new(DialogueAct.HumanAssistance, interpretation, true);
        if (interpretation.Directive == ConversationDirective.CancelCurrentFlow) return new(DialogueAct.Cancellation, interpretation, true);
        // A backed informational act can use ChatFlow's authorized FAQ/profile
        // reads. An ambiguous General intent alone must still ask for clarification.
        if (context.CurrentGoal == null && interpretation.Intent == ConversationIntent.General
            && interpretation.Directive == ConversationDirective.None && backedAct && interpretation.Act == DialogueAct.Information)
            return new(DialogueAct.Information, interpretation, true);
        if (interpretation.Intent == ConversationIntent.Request && context.CurrentGoal == null
            && backedAct && interpretation.Act == DialogueAct.StartOperation)
            return new(DialogueAct.StartOperation, interpretation, true);
        if (booking && (interpretation.Directive == ConversationDirective.ChangeLocation && local.Location.Status != ResolutionStatus.Determined
            || interpretation.Directive == ConversationDirective.ChangeService && local.Service.Status != ResolutionStatus.Determined
            || interpretation.Directive == ConversationDirective.ShowOptions))
            return new(DialogueAct.Correction, new() { Intent = ConversationIntent.Reservation, Directive = interpretation.Directive }, true);
        if (interpretation.Intent == ConversationIntent.Reservation || booking && interpretation.Intent != ConversationIntent.Order && interpretation.Intent != ConversationIntent.Request)
        {
            if (!booking && act != DialogueAct.StartOperation)
                return new(DialogueAct.OperationInformation, new() { Intent = ConversationIntent.Reservation }, true, DialoguePolicy.Instruction);
            // A semantic result can require entities not loaded for a simple-slot attempt.
            if (!booking && (interpretation.Service != null && local.AuthorizedServices.Count == 0 || interpretation.Location != null && local.AuthorizedLocations.Count == 0))
                local = await slots.ResolveAsync(workspace, request.MessageId, request.MessageText, context, SlotResolutionPurpose.Reservation, new(true, true, false), ct);
            local = AiEvidenceValidator.Validate(local, interpretation, request.MessageText);
            return PrepareBooking(local, act, true, workspace, request.MessageId);
        }
        return new(interpretation.RequiresClarification || interpretation.Intent == ConversationIntent.General ? DialogueAct.SemanticAmbiguity : act, interpretation, true);
    }

    private static PreparedDialogueTurn SelectReference(ReservationTimeReference reference, bool usedAi) =>
        new(DialogueAct.RequestedData, new() { Intent = ConversationIntent.Reservation,
            Location = reference.LocationId, Service = reference.ServiceId, Date = reference.Date, Time = reference.Time }, usedAi);

    private static PreparedDialogueTurn PrepareBooking(TurnUnderstanding value, DialogueAct act, bool usedAi, Guid workspace, string source, bool readOnly = false)
    {
        var unresolved = Unresolved(value);
        var question = Question(value, unresolved.FirstOrDefault());
        var proposal = new BookingDraftProposal(workspace, source, value.Context.StateVersion, value, question, unresolved, readOnly);
        var count = new[] { value.Name.Status, value.Location.Status, value.Service.Status, value.Date.Status, value.Time.Status }.Count(s => s == ResolutionStatus.Determined);
        return new(!readOnly && count > 1 && act != DialogueAct.Correction ? DialogueAct.MultipleData : act,
            new() { Intent = ConversationIntent.Reservation, BookingProposal = proposal }, usedAi);
    }

    private static List<string> Unresolved(TurnUnderstanding value)
    {
        var result = new List<string>();
        Add("Location", value.Location); Add("Service", value.Service); Add("Date", value.Date); Add("Time", value.Time); Add("Name", value.Name);
        return result;
        void Add<T>(string name, SlotResolution<T> resolution)
        {
            if (resolution.Status == ResolutionStatus.Ambiguous || resolution.Status == ResolutionStatus.Invalid && resolution.Code != "ModuleDenied"
                || resolution.Status == ResolutionStatus.Insufficient && resolution.Candidates.Count > 0)
                result.Add(name);
        }
    }

    private static string? Question(TurnUnderstanding value, string? slot) => slot switch
    {
        "Name" => "Para registrar la reserva necesito tu nombre y apellido. ¿Cómo te llamas?",
        "Location" => "Necesito precisar la sede. " + string.Join(", ", value.Location.Candidates.Select(c => c.Value.Name).Take(5)) + " ¿En cuál deseas atenderte?",
        "Service" => "Necesito precisar el servicio. " + string.Join(", ", value.Service.Candidates.Select(c => c.Value.Name).Take(5)) + " ¿Cuál deseas reservar?",
        "Date" => "Necesito una fecha inequívoca y vigente. ¿Qué día y mes prefieres?",
        "Time" => "Necesito precisar la hora. Puedes indicar una hora en formato 24 horas o una franja. ¿Qué horario prefieres?",
        _ => null
    };

}

