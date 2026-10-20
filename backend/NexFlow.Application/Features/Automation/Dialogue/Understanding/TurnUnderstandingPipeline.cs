using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

public interface ITurnUnderstandingPipeline
{
    Task<PreparedDialogueTurn> PrepareAsync(Guid workspace, string phone, ProcessIncomingMessageCommand request,
        ConversationContextDto context, HashSet<string> modules, CancellationToken ct);
}

public sealed class TurnUnderstandingPipeline(IWorkspaceSlotResolver slots, DialoguePolicy policy,
    IAiInterpreter interpreter, IConversationRecoveryService recovery) : ITurnUnderstandingPipeline
{
    public async Task<PreparedDialogueTurn> PrepareAsync(Guid workspace, string phone, ProcessIncomingMessageCommand request,
        ConversationContextDto context, HashSet<string> modules, CancellationToken ct)
    {
        try { return await PrepareCoreAsync(workspace, phone, request, context, modules, ct); }
        catch (BusinessProfileUnavailableException) when (!ct.IsCancellationRequested)
        {
            return new(DialogueAct.BusinessUnavailable, new() { Intent = ConversationIntent.ProviderUnavailable }, Response:
                "No pude consultar los datos del negocio en este momento. Tu borrador sigue guardado. Inténtalo nuevamente en un momento o escribe asesor.");
        }
    }

    private async Task<PreparedDialogueTurn> PrepareCoreAsync(Guid workspace, string phone, ProcessIncomingMessageCommand request,
        ConversationContextDto context, HashSet<string> modules, CancellationToken ct)
    {
        var act = policy.Classify(request.MessageText, context);
        if (act == DialogueAct.OperationInformation)
            return new(act, new() { Intent = ConversationIntent.Reservation }, Response:
                AiIntentAccess.GetDeniedResponse(ConversationIntent.Reservation, modules) ?? DialoguePolicy.Instruction);
        var booking = context.CurrentGoal is "BOOKING" or "RESERVATION" || act == DialogueAct.StartOperation;
        var normalized = SpanishTextNormalizer.Normalize(request.MessageText);
        var reservationCandidate = !booking && context.CurrentGoal == null && modules.Contains("RESERVATIONS") && modules.Contains("SERVICES")
            && normalized.Contains("quiero", StringComparison.Ordinal) && (normalized.Contains(" en ", StringComparison.Ordinal) || normalized.Contains("soy ", StringComparison.Ordinal));
        var purpose = booking || reservationCandidate ? SlotResolutionPurpose.Reservation : context.CurrentGoal == "ORDER" ? SlotResolutionPurpose.Order : SlotResolutionPurpose.Information;
        var local = await slots.ResolveAsync(workspace, request.MessageId, request.MessageText, context, purpose, policy.Reads(request.MessageText, context, act), ct);
        if (reservationCandidate && act == DialogueAct.SemanticAmbiguity && local.Service.Status == ResolutionStatus.Determined
            && (local.Date.Status == ResolutionStatus.Determined || local.Time.Status == ResolutionStatus.Determined)
            && (local.Name.Status == ResolutionStatus.Determined || local.Location.Status == ResolutionStatus.Determined))
        { booking = true; act = DialogueAct.StartOperation; }
        if (context.PendingCustomerName != null && local.Name.Code == "SurnameMissing" && context.CurrentStep == "COLLECT_CUSTOMERNAME")
            local = local with { Name = new NameSlotResolver().Resolve(context.PendingCustomerName + " " + local.Name.Candidates[0].Value, true) };
        var dataTurn = booking && act is DialogueAct.StartOperation or DialogueAct.RequestedData or DialogueAct.Correction;
        var understood = local.Name.Status == ResolutionStatus.Determined || local.Location.Status == ResolutionStatus.Determined
            || local.Service.Status == ResolutionStatus.Determined || local.Date.Status == ResolutionStatus.Determined || local.Time.Status == ResolutionStatus.Determined
            || Unresolved(local).Count > 0;
        if (dataTurn && (understood || act == DialogueAct.StartOperation))
            return PrepareBooking(local, act, false, workspace, request.MessageId);
        if (!await recovery.AcquireAiAsync(workspace, phone, request.MessageId, ct))
            return new(DialogueAct.ProviderFailure, new() { Intent = ConversationIntent.RateLimited }, Response:
                "Hemos alcanzado el límite temporal de consultas. Espera un minuto o escribe asesor para recibir ayuda humana.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        AiInterpretation interpretation;
        try
        {
            interpretation = interpreter is IContextualAiInterpreter contextual
                ? await contextual.InterpretAsync(request.MessageText, local.Context, deadline.Token)
                : await interpreter.InterpretAsync(workspace, request.MessageText, context.CurrentGoal ?? "", context.CurrentStep, modules, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { interpretation = new() { Intent = ConversationIntent.ProviderUnavailable, Failure = NexFlow.Application.Engines.AI.AiFailureKind.Timeout }; }
        if (interpretation.Intent is ConversationIntent.ProviderUnavailable or ConversationIntent.RateLimited)
            return new(DialogueAct.ProviderFailure, interpretation, true);
        // Informational interruptions never turn extracted entities into mutations of a business draft.
        if (interpretation.Directive == ConversationDirective.None && interpretation.Intent is ConversationIntent.ProductQuery
            or ConversationIntent.ServiceQuery or ConversationIntent.Location or ConversationIntent.BusinessHours or ConversationIntent.Faq)
            return new(booking ? DialogueAct.InformationalInterruption : DialogueAct.Information, interpretation, true);
        if (interpretation.Intent == ConversationIntent.Support) return new(DialogueAct.HumanAssistance, interpretation, true);
        if (interpretation.Directive == ConversationDirective.CancelCurrentFlow) return new(DialogueAct.Cancellation, interpretation, true);
        if (booking && (interpretation.Directive == ConversationDirective.ChangeLocation && local.Location.Status != ResolutionStatus.Determined
            || interpretation.Directive == ConversationDirective.ChangeService && local.Service.Status != ResolutionStatus.Determined
            || interpretation.Directive == ConversationDirective.ShowOptions))
            return new(DialogueAct.Correction, new() { Intent = ConversationIntent.Reservation, Directive = interpretation.Directive }, true);
        if (interpretation.Intent == ConversationIntent.Reservation || booking && interpretation.Intent != ConversationIntent.Order && interpretation.Intent != ConversationIntent.Request)
        {
            if (!booking && act != DialogueAct.StartOperation)
                return new(DialogueAct.OperationInformation, new() { Intent = ConversationIntent.Reservation }, true, DialoguePolicy.Instruction);
            // A semantic result can require entities not loaded for a simple-slot attempt.
            if (interpretation.Service != null && local.AuthorizedServices.Count == 0 || interpretation.Location != null && local.AuthorizedLocations.Count == 0)
                local = await slots.ResolveAsync(workspace, request.MessageId, request.MessageText, context, SlotResolutionPurpose.Reservation, new(true, true, false), ct);
            local = ValidateAi(local, interpretation, request.MessageText);
            return PrepareBooking(local, act, true, workspace, request.MessageId);
        }
        return new(interpretation.RequiresClarification || interpretation.Intent == ConversationIntent.General ? DialogueAct.SemanticAmbiguity : act, interpretation, true);
    }

    private static PreparedDialogueTurn PrepareBooking(TurnUnderstanding value, DialogueAct act, bool usedAi, Guid workspace, string source)
    {
        var unresolved = Unresolved(value);
        var question = Question(value, unresolved.FirstOrDefault());
        var proposal = new BookingDraftProposal(workspace, source, value.Context.StateVersion, value, question, unresolved);
        var count = new[] { value.Name.Status, value.Location.Status, value.Service.Status, value.Date.Status, value.Time.Status }.Count(s => s == ResolutionStatus.Determined);
        return new(count > 1 && act != DialogueAct.Correction ? DialogueAct.MultipleData : act,
            new() { Intent = ConversationIntent.Reservation, BookingProposal = proposal }, usedAi);
    }

    private static List<string> Unresolved(TurnUnderstanding value)
    {
        var result = new List<string>();
        Add("Name", value.Name); Add("Location", value.Location); Add("Service", value.Service); Add("Date", value.Date); Add("Time", value.Time);
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

    private static TurnUnderstanding ValidateAi(TurnUnderstanding value, AiInterpretation ai, string original)
    {
        var normalized = SpanishTextNormalizer.Normalize(original);
        bool Supplied(string text) => SpanishTextNormalizer.ContainsPhrase(normalized, SpanishTextNormalizer.Normalize(text));
        SlotResolution<ResolvedBusinessEntity> Entity(SlotResolution<ResolvedBusinessEntity> local, string? extracted, IReadOnlyList<BusinessEntityOption> allowed)
            => local.Status is ResolutionStatus.Determined or ResolutionStatus.Ambiguous || extracted == null ? local
                : Supplied(extracted) ? new OfferingMentionResolver().Resolve(extracted, allowed, true)
                : SlotResolution<ResolvedBusinessEntity>.Invalid("UnsubstantiatedEntity");
        var name = value.Name;
        if (name.Status == ResolutionStatus.Insufficient && name.Candidates.Count == 0 && ai.CustomerName != null)
            name = Supplied(ai.CustomerName) ? new NameSlotResolver().Resolve(ai.CustomerName, true) : SlotResolution<string>.Invalid("UnsubstantiatedName");
        var date = value.Date;
        if (date.Status == ResolutionStatus.Insufficient && ai.Date != null)
        {
            date = Supplied(ai.Date) ? new BusinessDateResolver().Resolve(ai.Date, value.Context.LocalBusinessNow, true)
                : SlotResolution<DateOnly>.Invalid("UnsubstantiatedDate");
        }
        var time = value.Time;
        if (time.Status == ResolutionStatus.Insufficient && ai.Time != null)
            time = Supplied(ai.Time) ? new TimePreferenceResolver().Resolve(ai.Time, true) : SlotResolution<TimePreference>.Invalid("UnsubstantiatedTime");
        return value with { Name = name, Location = Entity(value.Location, ai.Location, value.AuthorizedLocations),
            Service = Entity(value.Service, ai.Service, value.AuthorizedServices), Date = date, Time = time };
    }
}
