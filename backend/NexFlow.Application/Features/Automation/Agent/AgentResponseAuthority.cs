using System.Globalization;
using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.Dialogue.Composition;
using NexFlow.Application.Features.Knowledge;

namespace NexFlow.Application.Features.Automation.Agent;

// Semantic choices come from the model. This realizes acts and repository facts,
// never scans user phrases to infer a goal and never publishes free model prose.
public static class AgentResponseAuthority
{
    public static (AgentTurnResult Result, ConversationContextDto? Next) Prepare(AgentTurnRequest request,
        ConversationContextDto origin, AgentTurnResult proposal, IReadOnlySet<string> modules, DateTime now, AgentDraftStage? staged = null)
    {
        if (!proposal.Accepted || proposal.Proposal == null)
            return (AgentSafeReplies.Failure(proposal.Outcome, FailureText(proposal.Outcome)) with {
                ModelCalls = proposal.ModelCalls, ToolCalls = proposal.ToolCalls, ElapsedMilliseconds = proposal.ElapsedMilliseconds, Failure = proposal.Failure }, null);
        var plan = proposal.Proposal;
        if (plan.Conversation is { } conversation && (!Enum.IsDefined(conversation.Goal) || !Enum.IsDefined(conversation.Speech)
            || conversation.Need?.Length > 200 || conversation.References?.Length > 5)) return (AgentSafeReplies.Failure("ConversationPlanRejected"), null);
        if (plan.Conversation?.References?.Any(r => r.ReadIndex < 0 || r.ReadIndex >= proposal.Reads.Count
            || !(plan.FactReferences ?? []).Contains(r.ReadIndex) || !proposal.Reads[r.ReadIndex].Facts.Any(f => f.EntityId == r.EntityId)) == true)
            return (AgentSafeReplies.Failure("FactReferenceRejected"), null);
        if (plan.Conversation?.NeedsClarification == true || plan.Decision == AgentDecision.PrepareReservation
            && plan.Conversation is { Goal: not AgentCommunicativeGoal.BookingPreparation }) plan = plan with { Decision = AgentDecision.Clarify };
        if (plan.Narrative is { } narrative && (!Enum.IsDefined(narrative.Opening)
            || narrative.NextQuestion.HasValue && !Enum.IsDefined(narrative.NextQuestion.Value)))
            return (AgentSafeReplies.Failure("NarrativeRejected"), null);
        var next = staged?.Context ?? JsonSerializer.Deserialize<ConversationContextDto>(JsonSerializer.Serialize(origin))!;
        var question = ConversationQuestion.Current(request.WorkspaceId, origin, now);
        var booking = origin.CurrentGoal is "BOOKING" or "RESERVATION";
        var expected = ExpectedReply.Information; var keepQuestion = false; string text;
        var allowedBooking = modules.Contains("SERVICES") && modules.Contains("RESERVATIONS");
        switch (plan.Decision)
        {
            case AgentDecision.PrepareReservation:
                if (!allowedBooking || origin.Focus != null || origin.CurrentGoal is not (null or "BOOKING" or "RESERVATION"))
                    return (AgentSafeReplies.Failure("PreparationDenied", "Puedo ayudarte con información del negocio. No he cambiado lo que estabas preparando."), null);
                if (staged?.Clarification != null) {
                    next.RecoveryOptions = (staged.Choices ?? []).ToList(); next.RecoveryOptionsContext = "AGENT_OPTIONS";
                    text = staged.Clarification + (next.RecoveryOptions.Count > 0 ? "\n\n" + Choices(next.RecoveryOptions) + "\n\n¿Cuál deseas elegir?" : " ¿Puedes indicarlo de nuevo?");
                    expected = next.RecoveryOptions.Count > 0 ? ExpectedReply.Selection : ExpectedReply.RequestedData; break;
                }
                next.CurrentGoal = "RESERVATION";
                var askLocation = plan.Narrative?.NextQuestion == AgentNextQuestion.Location;
                if (staged != null) next.CurrentStep = AgentBookingDrafts.MissingStep(next);
                else if (!booking || next.CurrentStep == null) next.CurrentStep = askLocation ? "COLLECT_LOCATION" : "COLLECT_SERVICE";
                text = "Te ayudo a preparar una cita, sin registrarla todavía. " + (next.CurrentStep == "AGENT_PREVIEW"
                    ? "Ya tenemos los datos para revisarlos. Esta evaluación no registra citas. ¿Qué te gustaría consultar o cambiar?"
                    : next.PreferredTime != null && next.TargetTime == null && next.CurrentStep == "COLLECT_TIME"
                        ? "Conservé tu preferencia horaria. ¿Qué hora te gustaría que comprobemos?" : ConversationQuestion.RequestedDataPrompt(next));
                if (staged?.AvailabilityExplanation != null) text = staged.AvailabilityExplanation + "\n\n" + text;
                if (plan.Conversation != null && (plan.FactReferences?.Length ?? 0) > 0) {
                    var blocks = RenderFacts(plan, proposal.Reads); if (blocks == null) return (AgentSafeReplies.Failure("UnsafeFactShape"), null);
                    text = blocks + "\n\n" + text;
                }
                expected = ExpectedReply.RequestedData; break;
            case AgentDecision.ClarifyCancellationScope:
                if (!booking || !allowedBooking || origin.Focus != null) return (AgentSafeReplies.Failure("NoActivePreparation"), null);
                next.PendingRecoveryAction = "AGENT_CANCEL_SCOPE"; expected = ExpectedReply.DiscardDrafts;
                text = "¿Te refieres a la cita que estamos preparando o a una cita ya registrada?"; break;
            case AgentDecision.ProposeDiscardDraft:
                if (!booking || !allowedBooking || origin.Focus != null || question == null || question.ConversationId != request.ConversationId
                    || question.QuestionId != plan.QuestionId || origin.PendingRecoveryAction is not ("AGENT_CANCEL_SCOPE" or "AGENT_CANCEL_CONFIRM"))
                    return (AgentSafeReplies.Failure("DiscardReferenceRejected"), null);
                if (origin.PendingRecoveryAction == "AGENT_CANCEL_CONFIRM" && question.Expected == ExpectedReply.RecoveryDecision
                    && ConversationConfirmation.IsAffirmative(request.Text)) {
                    ClearPreparation(next); text = "He dejado de preparar esta cita. No he cancelado ninguna cita registrada. ¿En qué más puedo ayudarte?";
                } else {
                    next.PendingRecoveryAction = "AGENT_CANCEL_CONFIRM"; expected = ExpectedReply.RecoveryDecision;
                    text = "Entendido: la cita que estamos preparando. ¿Quieres dejar de prepararla? Puedes responder «Sí» si lo deseas.";
                }
                break;
            case AgentDecision.RetainDraft:
                if (question == null || question.ConversationId != request.ConversationId || question.QuestionId != plan.QuestionId
                    || origin.PendingRecoveryAction != "AGENT_CANCEL_CONFIRM" || !ConversationConfirmation.IsNegative(request.Text))
                    return (AgentSafeReplies.Failure("DiscardDecisionRejected"), null);
                next.PendingRecoveryAction = null; text = "Conservamos tu preparación. ¿Qué te gustaría consultar o ajustar?"; break;
            case AgentDecision.HumanAssistance:
                next.Mode = "Human"; next.HandoffAt = now; next.HandoffReason = "AgentHandoffRequested";
                if (next.PendingRecoveryAction is "AGENT_CANCEL_SCOPE" or "AGENT_CANCEL_CONFIRM") next.PendingRecoveryAction = null;
                text = "Puedes recibir ayuda de una persona del negocio. Conservamos lo que estabas preparando."; break;
            default:
                var refs = plan.FactReferences ?? [];
                if (refs.Length > 3 || refs.Distinct().Count() != refs.Length) return (AgentSafeReplies.Failure("FactReferenceRejected"), null);
                var paragraphs = new List<string>();
                foreach (var index in refs) {
                    if (index < 0 || index >= proposal.Reads.Count) return (AgentSafeReplies.Failure("FactReferenceRejected"), null);
                    var read = proposal.Reads[index];
                    if (read.Status != BusinessReadStatus.Found) return (AgentSafeReplies.Failure("ReadUnavailable"), null);
                    var selected = SelectFacts(plan, read); var block = selected == null ? null : Facts(selected, plan.Conversation?.Speech == AgentSpeechAct.Orient);
                    if (block == null) return (AgentSafeReplies.Failure("UnsafeFactShape"), null);
                    paragraphs.Add(block);
                }
                text = paragraphs.Count > 0 ? string.Join("\n\n", paragraphs) + "\n\n" + NextQuestion(plan, modules, false)
                    : proposal.Reads.Any(r => r.Status == BusinessReadStatus.Denied) ? "No puedo consultar esa información para este negocio. Puedes pedir ayuda a una persona."
                    : proposal.Reads.Any(r => r.Status == BusinessReadStatus.Unavailable) ? "No pude consultar esos datos ahora. Conservamos lo que preparabas; puedes volver a preguntar o pedir ayuda."
                    : proposal.Reads.Any(r => r.Status == BusinessReadStatus.NotFound) ? "No encuentro una opción registrada que coincida. Puedo revisar otra alternativa o ayudarte a pedir orientación al negocio. ¿Qué prefieres?"
                    : "Puedo orientarte sobre los servicios, productos o sedes que el negocio tenga registrados. " + NextQuestion(plan, modules, false);
                keepQuestion = origin.PendingRecoveryAction is "AGENT_CANCEL_SCOPE" or "AGENT_CANCEL_CONFIRM";
                if (!keepQuestion && plan.Conversation is { Speech: AgentSpeechAct.Orient, Goal: AgentCommunicativeGoal.Information or AgentCommunicativeGoal.Clarification }
                    && refs.Length == 1 && proposal.Reads[refs[0]] is { Source: "Services" or "Locations" } offered) {
                    var names = SelectFacts(plan, offered)!.Facts.Where(f => (f.Key is "Offering" or "LocationName") && f.EntityId != null).Take(5).ToArray();
                    if (names.Length > 0) {
                        next.RecoveryOptions = names.Select(f => new ConversationMenuOption(f.EntityId!, f.Value,
                            offered.Source == "Services" ? "AGENT_SERVICE" : "AGENT_LOCATION")).ToList();
                        next.RecoveryOptionsContext = "AGENT_OPTIONS"; expected = ExpectedReply.Selection;
                        text = string.Join("\n\n", paragraphs) + "\n\n¿Sobre cuál te gustaría continuar?";
                    }
                }
                break;
        }
        if (plan.Conversation != null) text = plan.Conversation.Speech switch {
            AgentSpeechAct.Orient => "Puedo orientarte con las opciones del negocio.\n\n" + text,
            AgentSpeechAct.Repair => "Gracias por aclararlo; lo vemos con más calma.\n\n" + text,
            AgentSpeechAct.Reassure => "Podemos ir paso a paso.\n\n" + text,
            AgentSpeechAct.Resume => "Seguimos con lo que preparabas.\n\n" + text,
            _ => text
        };
        if (plan.Narrative?.Opening == AgentOpening.Warm) text = "¡Hola! " + text;
        else if (plan.Narrative?.Opening == AgentOpening.Acknowledge) text = "Entiendo. " + text;
        if (!keepQuestion) ConversationQuestion.Remember(request.WorkspaceId, request.SourceMessageId, next, expected,
            booking || plan.Decision == AgentDecision.PrepareReservation ? NexFlow.Domain.Enums.ConversationIntent.Reservation : NexFlow.Domain.Enums.ConversationIntent.General,
            text, now, request.ConversationId);
        // Persist no literal evidence, free narrative, tool instructions or duplicate draft.
        var sanitized = plan with { CandidateResponse = "", Evidence = "", Tools = null,
            Conversation = plan.Conversation is { } semantic ? semantic with { Need = null, Booking = null } : null };
        var required = proposal.Reads.Where((_, i) => (plan.FactReferences ?? []).Contains(i)).Select(r => r.Source switch {
            "Services" => "SERVICES", "Products" => "CATALOG", "Locations" => "LOCATIONS", "BusinessHours" => "BUSINESS_HOURS", _ => ""
        }).Where(s => s.Length > 0).Distinct();
        return (proposal with { Proposal = sanitized, Reads = [], VerifiedText = text, RequiredModules = required.ToArray(),
            Handoff = plan.Decision == AgentDecision.HumanAssistance ? AgentHandoffState.Requested : AgentHandoffState.None }, next);
    }

    public static string FailureText(string outcome) => outcome switch {
        "InternalBudget" => "Ahora no puedo hacer otra consulta de interpretación. Conservamos tus datos; puedes enviar una nueva consulta más tarde o pedir ayuda al negocio.",
        "ProviderTimeout" => "La consulta tardó más de lo esperado. No he cambiado tu preparación. Puedes volver a preguntar o pedir ayuda al negocio.",
        "ProviderRateLimited" => "El servicio de interpretación está temporalmente ocupado. No he cambiado tu preparación; puedes consultar de nuevo más tarde.",
        _ => AgentSafeReplies.Interrupted
    };
    private static string Choices(IEnumerable<ConversationMenuOption> choices) => string.Join("\n", choices.Select((c, i) => (i + 1) + ". «" + c.Label + "»"));
    private static AgentToolResult? SelectFacts(AgentProposal plan, AgentToolResult read)
    {
        var selection = plan.Conversation?.References?.Where(r => r.ReadIndex == read.Index).ToArray();
        if (selection == null || selection.Length == 0) return read;
        if (selection.Select(s => s.EntityId).Distinct().Count() != selection.Length
            || selection.Any(s => !read.Facts.Any(f => f.EntityId == s.EntityId))) return null;
        return read with { Facts = selection.SelectMany(s => read.Facts.Where(f => f.EntityId == s.EntityId)).ToArray() };
    }
    private static string? RenderFacts(AgentProposal plan, IReadOnlyList<AgentToolResult> reads)
    {
        var blocks = new List<string>();
        foreach (var index in plan.FactReferences ?? []) {
            if (index < 0 || index >= reads.Count || reads[index].Status != BusinessReadStatus.Found) return null;
            var selected = SelectFacts(plan, reads[index]); var block = selected == null ? null : Facts(selected);
            if (block == null) return null; blocks.Add(block);
        }
        return string.Join("\n\n", blocks);
    }
    private static string NextQuestion(AgentProposal p, IReadOnlySet<string> modules, bool booking) => p.Narrative?.NextQuestion switch {
        AgentNextQuestion.Location when modules.Contains("LOCATIONS") || booking => "¿En qué sede te gustaría atenderte?",
        AgentNextQuestion.Service when modules.Contains("SERVICES") => "¿Sobre qué servicio te gustaría saber más?",
        AgentNextQuestion.HumanHelp => "¿Prefieres recibir ayuda de una persona del negocio?",
        _ => booking ? "¿Qué servicio necesitas para tu cita?" : "¿Qué te gustaría consultar ahora?"
    };
    private static bool Label(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 160 && !value.Any(char.IsControl);
    private static string? Facts(AgentToolResult read, bool numbered = false)
    {
        if (read.Facts.Any(f => f.Source != VerifiedFactSource.BusinessRepository || f.Sensitive)) return null;
        if (read.Source is "Services" or "Products") {
            var blocks = new List<string>();
            foreach (var group in read.Facts.GroupBy(f => f.EntityId)) {
                var names = group.Where(f => f.Key == "Offering").ToArray(); var prices = group.Where(f => f.Key == "PriceMinorUnits").ToArray(); var currencies = group.Where(f => f.Key == "Currency").ToArray();
                if (group.Key == null || names.Length != 1 || prices.Length != 1 || currencies.Length != 1 || !Label(names[0].Value)
                    || !long.TryParse(prices[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var price) || price < 0
                    || currencies[0].Value.Length != 3 || !currencies[0].Value.All(char.IsAsciiLetter)) return null;
                blocks.Add((numbered ? (blocks.Count + 1) + ". " : "") + "«" + names[0].Value + "»\n" + OfferingResponseFormatter.Money(currencies[0].Value, price));
            }
            return blocks.Count is > 0 and <= 5 ? string.Join("\n\n", blocks)
                + (read.Source == "Products" ? "\nFigurar en el catálogo no confirma stock físico; el negocio debe verificarlo." : "\nSon opciones de catálogo; no es un diagnóstico ni confirma disponibilidad para una cita.") : null;
        }
        if (read.Source is "Profile" or "Locations") {
            var key = read.Source == "Profile" ? "BusinessName" : "LocationName";
            var values = read.Facts.Where(f => f.Key == key).ToArray();
            return values.Length is > 0 and <= 5 && values.All(f => Label(f.Value)) ? "Información registrada:\n" + string.Join("\n", values.Select((f, i) => (numbered ? (i + 1) + ". " : "• ") + "«" + f.Value + "»")) : null;
        }
        if (read.Source == "BusinessHours" && read.Facts.Count == 1 && read.Facts[0].Key == "BusinessHours"
            && read.Facts[0].Value.Length <= 1500) return "Horario registrado por el negocio:\n" + read.Facts[0].Value;
        return null;
    }
    private static void ClearPreparation(ConversationContextDto c)
    {
        c.CurrentGoal = null; c.CurrentStep = null; c.SelectedLocationId = null; c.SelectedServiceId = null;
        c.TargetDate = null; c.TargetTime = null; c.PreferredTime = null; c.RealCustomerName = null; c.PendingCustomerName = null;
        c.PendingServiceIds.Clear(); c.PendingDialogueSlots.Clear(); c.MissingFields.Clear(); c.ReservationCommitSourceMessageId = null;
        c.ReservationServiceOffset = 0; c.PendingRecoveryAction = null; c.RecoveryOptions.Clear(); c.PendingReferenceExpiresAt = null;
    }
}
