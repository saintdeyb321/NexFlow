using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Features.Automation.Dialogue.Policy;

public sealed class DialoguePolicy
{
    public static DialogueAct OrderAct(OrderTurnKind kind, IReadOnlyList<OrderEdit> edits, ConversationContextDto state) => kind switch {
        OrderTurnKind.Start => DialogueAct.StartOperation,
        OrderTurnKind.Summary or OrderTurnKind.Hypothetical => DialogueAct.Information,
        OrderTurnKind.Submit => DialogueAct.Confirmation,
        OrderTurnKind.Clarify => DialogueAct.SemanticAmbiguity,
        OrderTurnKind.Location => DialogueAct.Correction,
        _ when edits.Any(e=>e.Kind != OrderEditKind.Add) => DialogueAct.Correction,
        _ when edits.Count > 1 => DialogueAct.MultipleData,
        _ => state.CurrentGoal == "ORDER" ? DialogueAct.RequestedData : DialogueAct.StartOperation
    };

    public DialogueAct Classify(string text, ConversationContextDto state)
    {
        var normalized = SpanishTextNormalizer.Normalize(text);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (IsInformationOnly(normalized)) return DialogueAct.Information;
        var booking = state.CurrentGoal is "BOOKING" or "RESERVATION";
        if (ConversationConfirmation.IsCorrection(text)) return booking ? DialogueAct.Correction : DialogueAct.SemanticAmbiguity;
        if (ReservationOperationClassifier.Classify(text, booking) == ReservationOperationKind.EditActiveDraft)
            return DialogueAct.Correction;
        if (normalized is "quiero hablar con una persona" or "quiero hablar con alguien" or "necesito hablar con una persona") return DialogueAct.HumanAssistance;
        if (normalized is "ya no quiero continuar" or "no quiero continuar" or "no quiero seguir" or "no sigamos") return DialogueAct.Cancellation;
        if (normalized is "no quiero reservar" or "no deseo reservar" or "no necesito reservar") return DialogueAct.DeclineOperation;
        if ((booking || state.CurrentGoal == "ORDER") && IsResume(normalized)) return DialogueAct.Resume;
        if (booking && state.CurrentQuestion?.Expected is not (ExpectedReply.Information or ExpectedReply.StartReservation)
            && IsAcknowledgement(normalized)) return DialogueAct.Resume;
        if (booking && words.Any(w => w is "pedir" or "pedido" or "comprar" or "solicitud" or "reclamo")) return DialogueAct.SemanticAmbiguity;
        // A negated clause requires semantic scope; extracted nouns must not start an operation.
        if (words.Contains("no") && !normalized.Contains("no se como", StringComparison.Ordinal)) return DialogueAct.SemanticAmbiguity;
        if (HasContradictoryGoals(normalized)) return DialogueAct.SemanticAmbiguity;
        var reservationMention = words.Any(w => w.StartsWith("reserv", StringComparison.Ordinal) || w is "agendar" or "cita");
        var instruction = IsProcedureQuestion(normalized);
        if (reservationMention && (instruction || OrderMentionResolver.IsQuestion(text))) return DialogueAct.OperationInformation;
        var correction = words.Any(w => w.StartsWith("cambi", StringComparison.Ordinal) || w is "mejor" or "otra" or "otro" or "corregir");
        if (booking && correction) return DialogueAct.Correction;
        var question = words.Any(w => w is "cuanto" or "precio" or "cuesta" or "donde" or "horario" or "horarios" or "duracion" or "como" or "que" or "cual");
        if (question) return booking ? DialogueAct.InformationalInterruption : DialogueAct.Information;
        if (reservationMention && words.Any(w => w is "quiero" or "quisiera" or "necesito" or "deseo" or "reservar" or "agenda"))
            return DialogueAct.StartOperation;
        return booking ? DialogueAct.RequestedData : DialogueAct.SemanticAmbiguity;
    }

    public static bool IsInformationOnly(string normalized)
    {
        var words = normalized.Split(' ');
        return words.Any(w => w is "solo" or "solamente")
            && words.Any(w => w is "preguntando" or "consultando" or "informacion")
            && !words.Any(w => w is "confirma" or "confirmo" or "envia" or "envialo");
    }

    internal static bool IsProcedureQuestion(string normalized) => normalized.Contains("como ", StringComparison.Ordinal)
        || normalized.Contains("no se", StringComparison.Ordinal) || normalized.Split(' ').Contains("informacion")
        || normalized.Contains("orientacion para ", StringComparison.Ordinal) || normalized.Contains("orientacion sobre ", StringComparison.Ordinal);

    // An acknowledgement can resume dialogue; it is never transaction consent.
    public static bool IsAcknowledgement(string normalized) => normalized is "si" or "de acuerdo" or "esta bien" or "vale" or "ok";

    public static bool IsResume(string normalized)
    {
        var words = normalized.Split(' ');
        return words.Any(w => w is "sigamos" or "continuemos" or "continuar" or "retomemos" or "prosigamos")
            && words.All(w => w is "esta" or "bien" or "si" or "perfecto" or "excelente" or "ok" or "vale" or "de" or "acuerdo"
                or "entonces" or "podemos" or "por" or "favor" or "sigamos" or "continuemos" or "continuar" or "retomemos" or "prosigamos");
    }

    public static bool HasContradictoryGoals(string normalized)
    {
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Any(w => w.StartsWith("reserv", StringComparison.Ordinal) || w is "agendar" or "cita")
            && words.Any(w => w is "pedir" or "comprar" or "pedido" or "solicitud" or "reclamo");
    }

    public SlotReadPlan Reads(string text, ConversationContextDto state, DialogueAct act)
    {
        var normalized = SpanishTextNormalizer.Normalize(text);
        if (act is DialogueAct.Information or DialogueAct.InformationalInterruption) return new(false, false, false);
        var simple = new NameSlotResolver().Resolve(text, state.CurrentStep == "COLLECT_CUSTOMERNAME").Status == ResolutionStatus.Determined
            && !normalized.Contains("reserv", StringComparison.Ordinal) && !normalized.Contains("sede", StringComparison.Ordinal)
            && !normalized.Contains(" en ", StringComparison.Ordinal);
        var temporal = (act is DialogueAct.RequestedData or DialogueAct.Correction)
            && (BusinessDateResolver.IsOnlyDateExpression(text) || TimePreferenceResolver.IsOnlyTimeExpression(text));
        // A name-shaped answer can itself be a service/site label. At the name
        // step keep authorized candidates available for that validation.
        var detailsOnly = simple && state.CurrentStep != "COLLECT_CUSTOMERNAME" || temporal;
        if (state.CurrentGoal == "ORDER" || normalized.Split(' ').Any(w => w is "producto" or "productos" or "catalogo" or "comprar" or "pedido"))
            return new(false, false, true);
        return new(!detailsOnly, !detailsOnly,
            state.CurrentGoal == "ORDER" || normalized.Split(' ').Any(w => w is "producto" or "productos" or "catalogo" or "comprar" or "pedido"));
    }

    public static string ReservationProcess => "Para reservar elegimos una sede, un servicio, fecha y hora disponibles; luego completamos tus datos y te pedimos confirmar.";
    public static string Instruction => ReservationProcess + " ¿Quieres que te ayude a empezar?";

    public static string NextBookingStep(ConversationContextDto state)
    {
        // Question priority only: proposals can supply slots in any order.
        var slots = new (string Slot, string Step, bool Missing)[] {
            ("Location", "COLLECT_LOCATION", string.IsNullOrWhiteSpace(state.SelectedLocationId)),
            ("Service", "COLLECT_SERVICE", string.IsNullOrWhiteSpace(state.SelectedServiceId)),
            ("Date", "COLLECT_DATE", string.IsNullOrWhiteSpace(state.TargetDate)),
            ("Time", "COLLECT_TIME", string.IsNullOrWhiteSpace(state.TargetTime)),
            ("Name", "COLLECT_CUSTOMERNAME", string.IsNullOrWhiteSpace(state.RealCustomerName)) };
        return slots.FirstOrDefault(s => s.Missing || state.PendingDialogueSlots.Contains(s.Slot)).Step ?? "CONFIRM";
    }
}

