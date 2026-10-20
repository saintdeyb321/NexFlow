using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;

namespace NexFlow.Application.Features.Automation.Dialogue.Policy;

public sealed class DialoguePolicy
{
    public DialogueAct Classify(string text, ConversationContextDto state)
    {
        var normalized = SpanishTextNormalizer.Normalize(text);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var booking = state.CurrentGoal is "BOOKING" or "RESERVATION";
        if (booking && words.Any(w => w is "pedir" or "pedido" or "comprar" or "solicitud" or "reclamo")) return DialogueAct.SemanticAmbiguity;
        var reservationMention = words.Any(w => w.StartsWith("reserv", StringComparison.Ordinal) || w is "agendar" or "cita");
        var instruction = normalized.Contains("como ", StringComparison.Ordinal) || normalized.Contains("no se", StringComparison.Ordinal)
            || words.Contains("puedo") || words.Contains("puede");
        if (reservationMention && instruction) return DialogueAct.OperationInformation;
        var correction = words.Any(w => w.StartsWith("cambi", StringComparison.Ordinal) || w is "mejor" or "otra" or "otro" or "corregir");
        if (booking && correction) return DialogueAct.Correction;
        var question = words.Any(w => w is "cuanto" or "precio" or "cuesta" or "donde" or "horario" or "horarios" or "duracion" or "como" or "que" or "cual");
        if (question) return booking ? DialogueAct.InformationalInterruption : DialogueAct.Information;
        if (reservationMention && words.Any(w => w is "quiero" or "quisiera" or "necesito" or "deseo" or "reservar" or "agenda"))
            return DialogueAct.StartOperation;
        return booking ? DialogueAct.RequestedData : DialogueAct.SemanticAmbiguity;
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
        var detailsOnly = simple || temporal;
        if (state.CurrentGoal == "ORDER" || normalized.Split(' ').Any(w => w is "producto" or "productos" or "catalogo" or "comprar" or "pedido"))
            return new(false, false, true);
        return new(!detailsOnly, !detailsOnly,
            state.CurrentGoal == "ORDER" || normalized.Split(' ').Any(w => w is "producto" or "productos" or "catalogo" or "comprar" or "pedido"));
    }

    public static string Instruction => "Para reservar elegimos una sede, un servicio, fecha y hora disponibles; luego completamos tus datos y te pedimos confirmar. ¿Quieres que te ayude a empezar?";
}
