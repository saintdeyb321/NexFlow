using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Composition;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public sealed record RecoveryDecision(string? Response = null, AiInterpretation? Interpretation = null,
    DialogueAct Act = DialogueAct.Information, OperationStatus Operation = OperationStatus.Informational,
    ExpectedReply Expected = ExpectedReply.Information);
public interface IConversationRecoveryService
{
    Task<RecoveryDecision?> RecoverAsync(Guid workspace, string phone, string conversation, ProcessIncomingMessageCommand request, ConversationContextDto context, HashSet<string> modules, CancellationToken ct);
    Task<RecoveryDecision> ClarifyAsync(Guid workspace, string phone, string source, ConversationContextDto context, HashSet<string> modules, string introduction, CancellationToken ct, bool contextual = false, IReadOnlyList<ConversationMenuOption>? choices = null);
    Task<string> DecorateAsync(Guid workspace, string phone, ConversationContextDto context, HashSet<string> modules, string response, CancellationToken ct, IReadOnlyList<ConversationMenuOption>? verifiedChoices = null);
    Task<bool> AcquireAiAsync(Guid workspace, string phone, string source, CancellationToken ct);
}

public sealed class ConversationRecoveryService(IContextRecoveryService contexts, IConversationStateRepository states,
    IOfferingService offerings, ILocationRepository locations, IBusinessHoursRepository hours, IReservationEngine reservations,
    IClock clock, IOptions<AssistantRecoveryOptions> options, IBusinessProfileRepository profiles, IKnowledgeService knowledge,
    ILogger<ConversationRecoveryService>? logger = null) : IConversationRecoveryService
{
    private AssistantRecoveryOptions Limits => options.Value;
    public static string Normalize(string text) => string.Join(' ', new string(text.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static bool No(string text) => text is "no" or "no gracias";
    private static string Fingerprint(ConversationContextDto c) => JsonSerializer.Serialize(new {
        c.CurrentGoal, c.CurrentStep, c.SelectedLocationId, c.SelectedServiceId, c.TargetDate, c.TargetTime, c.PreferredTime
    });
    private const string ReservationScopeQuestion = "¿Te refieres a la reserva que estamos preparando o a una cita que ya confirmaste?";
    private static string Continuation(ConversationContextDto c) => c.PendingRecoveryAction == "RESERVATION_SCOPE" ? ReservationScopeQuestion
        : c.ReservationManagement != null ? c.ReservationManagement.NextQuestion
        : string.IsNullOrEmpty(c.CurrentGoal) ? "¿Qué necesitas consultar?"
        : (c.CurrentGoal is "BOOKING" or "RESERVATION" ? "" : "Tu borrador sigue guardado. ") + (c.CurrentStep switch {
            "COLLECT_CUSTOMERNAME" => SecretaryPresentation.AskName, "COLLECT_LOCATION" => "¿En qué sede te gustaría atenderte? Puedes indicar su nombre o decir «la primera».",
            "COLLECT_SERVICE" => SecretaryPresentation.ChooseService, "COLLECT_DATE" => SecretaryPresentation.AskDate,
            "COLLECT_TIME" => SecretaryPresentation.AskTime(c.PreferredTime != null), "CONFIRM" => SecretaryPresentation.ConfirmReservation,
            _ => c.LastQuestion ?? "Puedes continuar o escribir cancelar para descartar el borrador." });

    public Task<bool> AcquireAiAsync(Guid workspace, string phone, string source, CancellationToken ct)
        => ConversationDiagnostics.MeasureAsync(logger, "BudgetAdmission",
            () => states.TryConsumeAiBudgetAsync(workspace, phone, source, clock.UtcNow, Limits.ClientAiCallsPerMinute, Limits.WorkspaceAiCallsPerMinute, ct),
            admitted => admitted ? "Admitted" : "Denied");

    public async Task<RecoveryDecision?> RecoverAsync(Guid workspace, string phone, string conversation,
        ProcessIncomingMessageCommand request, ConversationContextDto context, HashSet<string> modules, CancellationToken ct)
    {
        var expiredDraft = !string.IsNullOrWhiteSpace(context.CurrentGoal) && context.LastUpdated < clock.UtcNow.AddHours(-Limits.ContextExpiryHours);
        if (context.RecoveryLastMessageId == request.MessageId)
            return new(context.RecoveryLastResponse, context.RecoveryLastInterpretation == null ? null : JsonSerializer.Deserialize<AiInterpretation>(context.RecoveryLastInterpretation),
                Expected: context.CurrentQuestion?.SourceMessageId == request.MessageId ? context.CurrentQuestion.Expected : ExpectedReply.Information);
        if (request.MessageText.Length > Limits.MaxInputCharacters)
            return await Remember("Tu mensaje es demasiado largo. Envíalo en partes más cortas y plantea una consulta por vez.");
        var greeting = GreetingRecognizer.Parse(request.MessageText);
        var text = Normalize(greeting.Content);
        var booking = context.CurrentGoal is "BOOKING" or "RESERVATION";
        var reservationOperation = ReservationOperationClassifier.Classify(request.MessageText, booking);
        // Formal draft CTAs, not an intent grammar for arbitrary language.
        if (text == "editar" && (context.CurrentGoal is "ORDER" or "BOOKING" or "RESERVATION") && !expiredDraft)
            return await Remember(booking ? "Conservamos tu borrador. ¿Qué dato deseas cambiar: sede, servicio, fecha, hora o nombre?"
                : "Conservamos tu borrador. ¿Qué producto, cantidad o sede deseas cambiar?",
                expected: booking ? ExpectedReply.EditReservationField : ExpectedReply.Information,
                referenceIntent: booking ? ConversationIntent.Reservation : null);
        if (Regex.IsMatch(text, @"\b(ignora (?:todas las |las )?instrucciones|system prompt|api key|revela (?:el )?(?:prompt|secreto)|actua como administrador|cambia (?:el )?workspace)\b", RegexOptions.CultureInvariant))
            return await Remember("Solo puedo ayudarte con información y operaciones autorizadas de este negocio. " + Continuation(context));
        if (!text.Split(' ').Contains("no") && (Regex.IsMatch(text, @"\b(asesor humano|hablar con (?:un |una )?(?:asesor|persona|humano)|atencion humana)\b") || text is "asesor" or "humano"))
            return await RememberInterpretation(new() { Intent = ConversationIntent.Support });
        var information = context.Information is { } saved && saved.UpdatedAt >= clock.UtcNow.AddHours(-Limits.ContextExpiryHours)
            && saved.DraftFingerprint == InformationalRequest.DraftFingerprint(context) ? saved : null;
        if (text == "ver mas" && context.PendingRecoveryAction == null && OfferingReference.Read(workspace, context) is { Broad: true })
            return null; // The existing commerce reader revalidates this page against current data/licenses.
        if (context.PendingRecoveryAction == "RESERVATION_SCOPE")
        {
            var reference = ReservationOperationClassifier.ReferenceScope(text);
            var current = booking && !expiredDraft && context.PendingReferenceExpiresAt > clock.UtcNow
                && context.RecoveryOptionsContext == InformationalRequest.DraftFingerprint(context);
            if (!current)
            {
                ClearReference();
                if (booking && !expiredDraft)
                {
                    context.PendingRecoveryAction = "RESERVATION_SCOPE";
                    context.PendingReferenceExpiresAt = clock.UtcNow.AddMinutes(10);
                    context.RecoveryOptionsContext = InformationalRequest.DraftFingerprint(context);
                }
                else if (expiredDraft && context.CurrentGoal != null)
                {
                    context.PendingRecoveryAction = "EXPIRE";
                    return await Remember("El borrador ha caducado. ¿Deseas descartarlo? Responde sí o no; ninguna cita registrada cambiará.");
                }
                return await Remember("La referencia anterior ya no está vigente. " + ReservationScopeQuestion);
            }
            if (text is "cancelar" or "dejar aclaracion")
            {
                ClearReference();
                return await Remember("He dejado esta aclaración. Tu borrador sigue guardado; puedes continuar cuando quieras.");
            }
            if (reference != ReservationReferenceScope.Unclear)
            {
                ClearReference();
                return await RememberInterpretation(new() { Intent = reference == ReservationReferenceScope.Registered ? ConversationIntent.Support : ConversationIntent.Reservation });
            }
            var publicInterruption = InformationalRequest.PublicTopic(text) != null || InformationalRequest.IsRepair(text)
                || OrderMentionResolver.IsQuestion(request.MessageText)
                || information?.Clarification != null && SelectionIndex(text) != 0;
            if (!publicInterruption) return await Remember(ReservationScopeQuestion);
        }
        if (InformationalRequest.ExistingReservation(text, booking))
            return await Remember("Todavía no puedo consultar, modificar ni cancelar una cita ya registrada por este chat. No he realizado ningún cambio. Escribe asesor para recibir atención humana."
                + (context.CurrentGoal != null ? " Tu borrador en preparación sigue guardado." : ""));
        if (reservationOperation == ReservationOperationKind.AmbiguousReservationOperation)
        {
            if (booking && context.PendingRecoveryAction == null && !expiredDraft)
            {
                context.PendingRecoveryAction = "RESERVATION_SCOPE";
                context.PendingReferenceExpiresAt = clock.UtcNow.AddMinutes(10);
                context.RecoveryOptions.Clear(); context.RecoveryOptionsContext = InformationalRequest.DraftFingerprint(context);
                return await Remember(ReservationScopeQuestion);
            }
            if (!booking) return await Remember("¿Deseas crear una reserva nueva o revisar una cita ya registrada?");
        }

        var question = ConversationQuestion.Current(workspace, context, clock.UtcNow);
        if (text == "que" && question != null)
        {
            var denied = AiIntentAccess.GetDeniedResponse(question.Intent, modules);
            var previousResponse = context.CurrentGoal == "ORDER"
                ? question.SourceMessageId == context.OrderLastSourceMessageId ? context.OrderLastResponse : question.LinguisticQuestion(context)
                : context.LastQuestion;
            return await Remember(denied ?? previousResponse ?? question.LinguisticQuestion(context),
                expected: denied == null ? question.Expected : ExpectedReply.Information, referenceIntent: question.Intent);
        }
        var repair = InformationalRequest.IsRepair(text);
        if (booking && context.CurrentStep == "COLLECT_TIME" && context.PendingRecoveryAction == null && !expiredDraft && InformationalRequest.Menu(text))
            return await RememberInterpretation(new() { Intent = ConversationIntent.Reservation, Directive = ConversationDirective.ShowOptions });
        var topic = repair ? InformationalRequest.RepairTopic(request.MessageText) : InformationalRequest.PublicTopic(text);
        if (topic != null || InformationalRequest.ScopeCorrection(text) && information != null)
        {
            if (topic == null && Enum.TryParse<KnowledgeTopic>(information!.Topic, out var previous)) topic = previous;
            if (topic != null) return await AnswerInformation(topic.Value, resetScope: InformationalRequest.ScopeCorrection(text));
        }
        if (information?.Clarification == "Availability" && context.PendingRecoveryAction == null
            && (ConversationConfirmation.IsAffirmative(request.MessageText) || DialoguePolicy.IsAcknowledgement(text) || repair))
        {
            var reference = ReservationTimeReference.Current(workspace, context, clock.UtcNow);
            if (reference != null && question?.Expected == ExpectedReply.SelectReservationTime
                && ConversationConfirmation.IsAffirmative(request.MessageText))
                return await RememberInterpretation(new() { Intent = ConversationIntent.Reservation,
                    Location = reference.LocationId, Service = reference.ServiceId, Date = reference.Date, Time = reference.Time });
            var continuation = reference == null
                ? "Indica la fecha y hora que deseas seleccionar, o escribe continuemos para retomar el borrador."
                : $"Consultamos el {DateOnly.ParseExact(reference.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture):dd/MM/yyyy} a las {reference.Time}. "
                    + $"¿Deseas elegir esa hora? Puedes responder «Sí» o indicar otra; volveré a verificarla.";
            return await Remember("No he cambiado ni confirmado tu reserva. " + continuation,
                expected: reference == null ? ExpectedReply.Information : question!.Expected, referenceIntent: ConversationIntent.Reservation);
        }
        if (repair)
        {
            if (information != null)
            {
                if (Enum.TryParse<KnowledgeTopic>(information.Topic, out var previousTopic)
                    && InformationalRequest.RepairTopic(request.MessageText, rejected: true) is { } rejected && rejected != previousTopic)
                    return await AnswerInformation(previousTopic);
                if (text.Split(' ').Contains("sede") && text.Split(' ').Contains("otra") && Enum.TryParse<KnowledgeTopic>(information.Topic, out var scopedTopic))
                    return await AnswerInformation(scopedTopic);
                information.Clarification = "Topic";
                var subject = information.Topic == nameof(KnowledgeTopic.BusinessHours) ? "los horarios de atención"
                    : information.Topic == nameof(KnowledgeTopic.Profile) ? "la información del negocio"
                    : information.Topic == nameof(KnowledgeTopic.Services) ? "los servicios"
                    : information.Topic == nameof(KnowledgeTopic.Products) ? "los productos" : "las ubicaciones";
                return await ClarifyAsync(workspace, phone, request.MessageId, context, modules,
                    $"Estábamos consultando {subject}. ¿Qué dato necesitas corregir o consultar?" + (context.CurrentGoal != null ? " Tu borrador sigue guardado." : ""), ct, contextual: true);
            }
            if (context.CurrentGoal != null) return await Remember("Conservamos los datos de tu borrador. " + Continuation(context));
            return await ClarifyAsync(workspace, phone, request.MessageId, context, modules,
                "Para corregir la respuesta, ¿qué información del negocio necesitas consultar?", ct, contextual: true);
        }
        if (DialoguePolicy.IsInformationOnly(text))
            return await Remember(information == null ? "De acuerdo, no iniciaré ninguna operación. ¿Qué información necesitas consultar?"
                : "De acuerdo, seguimos con la consulta informativa. No he cambiado tu borrador. Indica qué dato necesitas precisar.");
        if (information is { Clarification: "Location" })
        {
            // A yes to an informational question is not consent to an older transaction summary.
            if (ConversationConfirmation.IsAffirmative(request.MessageText))
                return await AnswerInformation(KnowledgeTopic.BusinessHours, resetScope: true);
            var selection = SelectionIndex(text);
            var named = information.Locations.Where(l => Normalize(l.Label) == text).ToArray();
            if (selection > 0 && selection <= information.Locations.Count)
                return await AnswerInformation(KnowledgeTopic.BusinessHours, information.Locations[selection - 1].Key);
            if (named.Length == 1) return await AnswerInformation(KnowledgeTopic.BusinessHours, named[0].Key);
            if (selection != 0) return await AnswerInformation(KnowledgeTopic.BusinessHours, resetScope: true);
            // A name outside the first page is resolved against the current authorized locations.
            if (modules.Contains("BUSINESS_HOURS") && !InformationalRequest.Menu(text) && !ConversationConfirmation.IsAffirmative(request.MessageText))
            {
                var currentLocations = await ReadInformationLocations();
                if (currentLocations == null) return await Remember(new KnowledgeResult { Status = KnowledgeStatus.Unavailable }.ToResponse());
                var candidates = currentLocations.Select(l => new BusinessEntityOption(l.Id!, l.Name)).ToArray();
                if (new LocationMentionResolver().Resolve(request.MessageText, candidates, directAnswer: true).TryGetValue(out var location))
                    return await AnswerInformation(KnowledgeTopic.BusinessHours, location.Id, knownLocations: currentLocations);
            }
        }
        if ((context.CurrentStep == "CONFIRM" || context.CurrentStep?.StartsWith("ORDER_CONFIRM:", StringComparison.Ordinal) == true || context.PendingRecoveryAction is "CANCEL" or "EXPIRE")
            && ConversationConfirmation.IsStatusQuestion(request.MessageText))
            return await Remember("La propuesta todavía espera tu decisión; esta pregunta no la confirma. " + Continuation(context));
        if (information?.Clarification == "Topic" && ConversationConfirmation.IsAffirmative(request.MessageText))
            return await Remember("Indica qué dato deseas corregir en esta consulta; para retomar el borrador, escribe continuemos. No he confirmado ninguna operación.");
        if ((context.CurrentStep == "CONFIRM" || context.CurrentStep?.StartsWith("ORDER_CONFIRM:", StringComparison.Ordinal) == true || context.PendingRecoveryAction is "CANCEL" or "EXPIRE")
            && ConversationConfirmation.IsNegative(request.MessageText))
        {
            context.PendingRecoveryAction = null;
            return await Remember("No he confirmado la operación. Conservamos el borrador; indica qué deseas corregir o escribe cancelar para descartarlo.");
        }
        if ((context.CurrentStep == "CONFIRM" || context.CurrentStep?.StartsWith("ORDER_CONFIRM:", StringComparison.Ordinal) == true || context.PendingRecoveryAction is "CANCEL" or "EXPIRE")
            && ConversationConfirmation.NeedsClarification(request.MessageText))
            return await Remember("No he confirmado ni cambiado la operación. Si confirmas el resumen vigente, responde «sí confirmo»; si deseas corregirlo, indica el cambio.");
        if (context.PendingRecoveryAction != null && Regex.IsMatch(text, @"\b(que|cual|cuanto|donde|como|precio|horarios?|direccion)\b"))
            return null;
        if (context.PendingRecoveryAction == "RESERVATION_SCOPE") return null; // Only a read-only interruption reached this point.
        if (context.PendingRecoveryAction == "REQUEST_DETAILS")
        {
            if (text is "cancelar" or "no") { context.PendingRecoveryAction = null; return await Remember("No he registrado ninguna solicitud."); }
            if (text.Length < 10 || ConversationConfirmation.IsAffirmative(request.MessageText)) return await Remember("Describe brevemente qué necesitas solicitar al negocio, o escribe cancelar.");
            context.PendingRecoveryAction = null;
            return await RememberInterpretation(new() { Intent = ConversationIntent.Request });
        }
        if (context.PendingRecoveryAction != null)
        {
            if (ConversationConfirmation.IsAffirmative(request.MessageText))
            {
                context.CurrentGoal = null; context.CurrentStep = null; context.SelectedServiceId = null; context.SelectedLocationId = null;
                context.TargetDate = null; context.TargetTime = null; context.OrderDraftItems.Clear(); context.MissingFields.Clear(); context.LastQuestion = null;
                context.ReservationCommitSourceMessageId = null; context.RecoveryOptionsOffset = 0; context.RecoveryOptionsContext = null;
                context.PendingCustomerName = null; context.PendingServiceIds.Clear(); context.PendingDialogueSlots.Clear(); context.PreferredTime = null;
                context.PendingRecoveryAction = null; context.RecoveryOptions.Clear(); context.ClarificationAttempts = 0;
                return await Remember(booking ? "He descartado esta reserva en preparación. No se ha cancelado ninguna reserva registrada."
                    : "He descartado únicamente el borrador. No se ha cancelado ninguna reserva ni pedido registrado.", DialogueAct.Cancellation, OperationStatus.Cancelled);
            }
            if (No(text)) { context.PendingRecoveryAction = null; return await Remember(Continuation(context)); }
            return await Remember(booking ? "¿Deseas dejar esta reserva en preparación? Responde sí o no. Para hablar con una persona escribe asesor."
                : "Confirma si deseas descartar el borrador: responde sí o no. Para hablar con una persona escribe asesor.");
        }
        if (!string.IsNullOrWhiteSpace(context.CurrentGoal) && context.LastUpdated < clock.UtcNow.AddHours(-Limits.ContextExpiryHours))
        {
            context.PendingRecoveryAction = "EXPIRE";
            return await Remember(booking ? "Ha pasado un tiempo desde tu última respuesta. ¿Deseas dejar esta reserva en preparación y comenzar de nuevo? Responde sí o no."
                : "El borrador lleva tiempo sin actualizarse. ¿Quieres descartarlo para empezar de nuevo? Responde sí o no; no realizaré operaciones con esos datos mientras confirmas.");
        }
        if (text is "descartar" or "cancelar" or "cancela" or "cancelar reserva" or "cancelar pedido" or "cancelar borrador" or "cancela la reserva" or "cancela el pedido")
        {
            if (string.IsNullOrEmpty(context.CurrentGoal)) return await Remember("No hay un borrador activo. Para revisar una reserva o pedido registrado escribe asesor.");
            context.PendingRecoveryAction = "CANCEL";
            return await Remember(booking ? "¿Deseas dejar esta reserva en preparación? Responde sí o no. Esto no cancela ninguna reserva registrada."
                : "¿Quieres descartar el borrador actual? Responde sí o no. Esto no cancela una reserva o pedido ya registrado.", DialogueAct.Cancellation, OperationStatus.AwaitingConfirmation);
        }
        if (context.CurrentGoal == "ORDER" && context.PendingRecoveryAction == null
            && (context.CurrentStep?.StartsWith("ORDER_CONFIRM:", StringComparison.Ordinal) == true
                || context.CurrentStep?.StartsWith("ORDER_REVIEW:", StringComparison.Ordinal) == true)
            && question?.Expected != ExpectedReply.Information && ConversationConfirmation.IsAffirmative(request.MessageText))
            return null; // OrderFlow validates the actual quote/question and renews expired or legacy reviews.
        if (context.PendingRecoveryAction == null && ConversationConfirmation.IsAffirmative(request.MessageText) && context.CurrentQuestion != null)
        {
            if (question == null)
                return booking && context.CurrentStep == "CONFIRM"
                    ? await RememberInterpretation(new() { Intent = ConversationIntent.Reservation })
                    : await Remember("La pregunta anterior ya no está vigente. " + Continuation(context));
            if (question.Expected == ExpectedReply.StartReservation)
            {
                var denied = AiIntentAccess.GetDeniedResponse(ConversationIntent.Reservation, modules);
                return denied != null ? await Remember(denied)
                    : await RememberInterpretation(new() { Intent = ConversationIntent.Reservation });
            }
            if (question.Expected == ExpectedReply.Information)
                return await Remember("Seguimos con la consulta informativa; no he confirmado ninguna operación. "
                    + (context.CurrentGoal != null ? "Escribe continuemos para retomar el borrador." : "¿Qué dato necesitas precisar?"));
        }
        if (context.CurrentStep == "CONFIRM" && booking && ConversationConfirmation.IsAffirmative(request.MessageText))
            return await RememberInterpretation(new() { Intent = ConversationIntent.Reservation,
                Confirmed = ConversationQuestion.CanConfirmReservation(workspace, context, request.MessageText, clock.UtcNow) });
        if (No(text) && context.CurrentStep == "CONFIRM") return await Remember("No he registrado la reserva. Puedes corregir la sede, servicio, fecha u hora, o escribir cancelar para dejar esta reserva en preparación.");
        if (greeting.HasGreeting && text.Length == 0)
            return await Remember(context.CurrentGoal != null ? Continuation(context)
                : context.RecoveryLastMessageId != null || context.LastQuestion != null || context.LastIntent != null
                    ? "¿En qué más puedo ayudarte? Si prefieres, escribe menú para ver opciones o asesor para hablar con una persona."
                    : "¡Hola! Soy el asistente del negocio. ¿Qué necesitas consultar? Si prefieres, escribe menú para ver opciones o asesor para hablar con una persona.");
        if (greeting.HasGreeting && text is "quisiera informacion" or "quiero informacion" or "necesito informacion")
            return await Remember("Claro, ¿qué información del negocio necesitas? También puedes escribir menú para ver las opciones disponibles o asesor para solicitar ayuda humana.");
        if (text is "necesito ayuda con un pedido" or "quiero ayuda con un pedido")
            return await Remember(AiIntentAccess.GetDeniedResponse(ConversationIntent.Order, modules)
                ?? (context.CurrentGoal != null ? Continuation(context)
                    : "¿Quieres preparar un pedido nuevo o consultar uno ya enviado? Para revisar un pedido enviado puedes solicitar un asesor."));
        if (text is "adios" or "chau" or "hasta luego" or "gracias" or "muchas gracias")
            return await Remember("¡Gracias por escribirnos! " + (string.IsNullOrEmpty(context.CurrentGoal) ? "Aquí estaremos cuando necesites información del negocio." : Continuation(context)));
        if (text is "quiero informacion" or "informacion" or "ayudame" or "ayuda" or "que ofrecen" or "menu" or "opciones" || InformationalRequest.Menu(text))
        { context.RecoveryOptionsOffset = 0; return await Menu("Estas son las opciones disponibles:"); }
        if (text is "ver mas" or "mas opciones" or "otros servicios")
        {
            if (context.RecoveryOptionsContext == Fingerprint(context) && context.RecoveryHasMore) context.RecoveryOptionsOffset += 5;
            return await Menu("Estas son las opciones disponibles:");
        }
        if (context.CurrentGoal == "ORDER" && context.CurrentStep?.StartsWith("ORDER_CONFIRM:", StringComparison.Ordinal) == true && ConversationConfirmation.IsAffirmative(request.MessageText)) return null;
        var index = SelectionIndex(text);
        // Recognize old labels too, but let the fingerprint guard below reject
        // them. A stale/ambiguous label must not fall through to AI selection.
        if (index == 0 && (context.CurrentQuestion == null || question?.Expected == ExpectedReply.Selection))
        {
            var matches = context.RecoveryOptions.Select((o, i) => (Option: o, Index: i + 1)).Where(o => Normalize(o.Option.Label) == text).ToArray();
            index = matches.Length > 1 ? -1 : matches.Length == 1 ? matches[0].Index : 0;
        }
        if (text is "esa opcion" or "esa" || ConversationConfirmation.IsAffirmative(request.MessageText)) index = context.RecoveryOptions.Count == 1 ? 1 : -1;
        if (text is "la otra" or "otra" or "esa otra") index = -1;
        if (index != 0)
        {
            if (context.CurrentQuestion != null && question?.Expected != ExpectedReply.Selection)
                return await Remember("La lista anterior no corresponde a la pregunta vigente. "
                    + (question?.LinguisticQuestion(context) ?? Continuation(context)), expected: question?.Expected ?? ExpectedReply.Information);
            if (context.LastUpdated < clock.UtcNow.AddHours(-Limits.ContextExpiryHours))
                return await Menu("La lista anterior ha caducado. Elige una opción vigente:");
            if (context.RecoveryOptionsContext != Fingerprint(context) || index < 1 || index > context.RecoveryOptions.Count)
                return await ClarifyAsync(workspace, phone, request.MessageId, context, modules,
                    context.RecoveryOptions.Count == 0 ? "Necesito una respuesta concreta para el paso actual." : "Necesito que elijas una opción de la lista actual.", ct);
            var selected = context.RecoveryOptions[index - 1];
            var catalogChoice = context.RecoveryOptions.All(o => o.Kind == "Action" && o.Key is "ProductQuery" or "ServiceQuery");
            var available = await BuildOptions(workspace, context, modules, ct, paginate: !catalogChoice, catalogOnly: catalogChoice);
            if (!available.Any(o => o.Key == selected.Key && o.Kind == selected.Kind)) return await Menu("Esa opción ya no está disponible. Elige una de estas:");
            context.RecoveryOptions.Clear(); context.ClarificationAttempts = 0;
            var interpretation = new AiInterpretation { Intent = selected.Kind == "Action" ? Enum.Parse<ConversationIntent>(selected.Key) : ConversationIntent.Reservation,
                QueryKind = OfferingQueryKind.Broad, Location = selected.Kind == "Location" ? selected.Key : null,
                Service = selected.Kind == "Service" ? selected.Key : null, Time = selected.Kind == "Time" ? selected.Key : null };
            if (!string.IsNullOrEmpty(context.CurrentGoal) && selected.Kind == "Action" && interpretation.Intent is ConversationIntent.Order or ConversationIntent.Reservation)
                return await Remember("Ya tienes un borrador activo. " + Continuation(context));
            if (selected.Kind == "Action" && interpretation.Intent == ConversationIntent.Request)
            { context.PendingRecoveryAction = "REQUEST_DETAILS"; return await Remember("¿Qué necesitas solicitar al negocio? Describe tu solicitud antes de registrarla."); }
            return await RememberInterpretation(interpretation);
        }
        if (No(text)) return await Remember(Continuation(context));
        return null;

        Task<RecoveryDecision> Remember(string response, DialogueAct act = DialogueAct.Information, OperationStatus operation = OperationStatus.Informational,
            ExpectedReply expected = ExpectedReply.Information, ConversationIntent? referenceIntent = null)
        {
            // An informational interruption must not renew the validity of an already expired draft.
            if (expiredDraft && context.CurrentGoal != null && context.PendingRecoveryAction == null) context.PendingRecoveryAction = "EXPIRE";
            return SaveAsync(workspace, phone, request.MessageId, context, new(response,
                referenceIntent is { } referenced ? new() { Intent = referenced } : null, Act: act, Operation: operation, Expected: expected), ct);
        }
        Task<RecoveryDecision> RememberInterpretation(AiInterpretation interpretation) => SaveAsync(workspace, phone, request.MessageId, context, new(Interpretation: interpretation), ct);
        void ClearReference()
        {
            context.PendingRecoveryAction = null; context.PendingReferenceExpiresAt = null;
            context.RecoveryOptionsContext = null; context.RecoveryOptions.Clear();
        }
        async Task<RecoveryDecision> Menu(string intro)
        {
            context.Information = null;
            var response = await PrepareOptionsAsync(workspace, context, modules, intro, ct);
            return await Remember(response, expected: ExpectedReply.Selection);
        }

        async Task<RecoveryDecision> AnswerInformation(KnowledgeTopic source, string? explicitLocation = null, bool resetScope = false, LocationDto[]? knownLocations = null)
        {
            var intent = source switch {
                KnowledgeTopic.BusinessHours => ConversationIntent.BusinessHours, KnowledgeTopic.Locations => ConversationIntent.Location,
                KnowledgeTopic.Services => ConversationIntent.ServiceQuery, KnowledgeTopic.Products => ConversationIntent.ProductQuery, _ => ConversationIntent.General };
            var denied = AiIntentAccess.GetDeniedResponse(intent, modules);
            if (denied != null) return await SaveAsync(workspace, phone, request.MessageId, context, new(denied, new() { Intent = intent }, Operation: OperationStatus.Denied), ct);
            if (source is KnowledgeTopic.Products or KnowledgeTopic.Services)
            {
                // Reuse ChatFlow's current-PDF/factual fallback and durable media lifecycle.
                if (expiredDraft && context.CurrentGoal != null && context.PendingRecoveryAction == null) context.PendingRecoveryAction = "EXPIRE";
                return await RememberInterpretation(new() { Intent = intent, QueryKind = OfferingQueryKind.Broad });
            }
            string? scope = null;
            if (source == KnowledgeTopic.BusinessHours && !resetScope)
            {
                var authorized = knownLocations ?? await ReadInformationLocations();
                if (authorized == null) return await SaveInformation(new KnowledgeResult { Status = KnowledgeStatus.Unavailable, Source = source });
                knownLocations = authorized;
                if (explicitLocation != null)
                {
                    if (authorized.Any(l => l.Id == explicitLocation)) scope = explicitLocation;
                    // An obsolete choice is not a licence to reuse a different contextual location.
                }
                else if (text.Split(' ').Contains("otra") && text.Split(' ').Contains("sede"))
                {
                    var remaining = authorized.Where(l => l.Id != information?.LocationId).ToArray();
                    if (information?.LocationId != null && remaining.Length == 1) scope = remaining[0].Id;
                    else return await SaveInformation(new KnowledgeResult { Status = KnowledgeStatus.Found, Source = source,
                        LocationChoices = remaining.Take(5).Select(l => new KnowledgeLocationChoice(l.Id!, l.Name)).ToArray(),
                        Facts = "¿De qué sede deseas consultar los horarios?\n" + string.Join("\n", remaining.Take(5).Select((l, i) => $"{i + 1}. {l.Name}")) });
                }
                else
                {
                    var resolved = new LocationMentionResolver().Resolve(request.MessageText, authorized.Select(l => new BusinessEntityOption(l.Id!, l.Name)).ToArray());
                    if (resolved.TryGetValue(out var matched)) scope = matched.Id;
                    else if (resolved.Status == ResolutionStatus.Ambiguous
                        || text.Split(' ').Contains("sede") && !text.Contains("esta sede", StringComparison.Ordinal) && !text.Contains("esa sede", StringComparison.Ordinal))
                    {
                        var choices = authorized.Take(5).Select(l => new KnowledgeLocationChoice(l.Id!, l.Name)).ToArray();
                        return await SaveInformation(new KnowledgeResult {
                            Status = KnowledgeStatus.Found, Source = source, LocationChoices = choices,
                            Facts = choices.Length > 0 ? "Necesito precisar la sede para consultar sus horarios. ¿Cuál deseas?\n"
                                + string.Join("\n", choices.Select((l, index) => $"{index + 1}. {l.Name}"))
                                : "No encuentro una sede vigente para esa consulta. Puedes solicitar un asesor."
                        });
                    }
                    else
                    {
                        var activeLocation = context.ReservationManagement?.Selected?.LocationId ?? context.SelectedLocationId;
                        var contextual = information?.Topic == source.ToString() ? information.LocationId ?? activeLocation : activeLocation;
                        if (text.Split(' ').Any(w => w is "general" or "generales")) contextual = null;
                        if (authorized.Any(l => l.Id == contextual)) scope = contextual;
                    }
                }
            }
            var result = await knowledge.QueryAsync(workspace, new() { WorkspaceId = workspace, CurrentQueryLocations = knownLocations }, new() { Topic = source, LocationId = scope }, ct);
            return await SaveInformation(result);

            async Task<RecoveryDecision> SaveInformation(KnowledgeResult result)
            {
                if (expiredDraft && context.CurrentGoal != null && context.PendingRecoveryAction == null) context.PendingRecoveryAction = "EXPIRE";
                if (result.Found) context.ClarificationAttempts = 0;
                InformationalRequest.Remember(context, source, result, clock.UtcNow);
                var response = result.ToResponse();
                if (context.CurrentGoal != null || context.ReservationManagement != null) response += "\n\n" + (context.PendingRecoveryAction is "CANCEL" or "EXPIRE"
                    ? "Sigue pendiente tu confirmación para descartar el borrador: responde sí o no."
                    : "Tu borrador sigue sin cambios. " + Continuation(context));
                return await SaveAsync(workspace, phone, request.MessageId, context, new(response, new() { Intent = intent }), ct);
            }
        }

        async Task<LocationDto[]?> ReadInformationLocations()
        {
            try { return (await locations.GetLocationsAsync(workspace, ct)).Where(l => l.Id != null).ToArray(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { ct.ThrowIfCancellationRequested(); return null; }
        }
    }

    internal static int SelectionIndex(string text)
    {
        // Match the whole utterance. Quantities, identifiers, dates, times and
        // mixed requests must never be extracted as a menu index.
        var match = Regex.Match(text,
            @"^(?:(?:escojo|elijo|selecciono) (?:la )?(?:(?:opcion|numero) )?|quiero la opcion |(?:la )?(?:(?:opcion|numero) )?)([0-9]{1,2}|uno|dos|tres|cuatro|cinco|primera|segunda|tercera|cuarta|quinta)$",
            RegexOptions.CultureInvariant);
        if (!match.Success) return 0;
        var index = match.Groups[1].Value switch
        {
            "uno" or "primera" => 1, "dos" or "segunda" => 2, "tres" or "tercera" => 3,
            "cuatro" or "cuarta" => 4, "cinco" or "quinta" => 5,
            var number => int.Parse(number, CultureInfo.InvariantCulture)
        };
        return index == 0 ? -1 : index;
    }

    public async Task<RecoveryDecision> ClarifyAsync(Guid workspace, string phone, string source, ConversationContextDto context, HashSet<string> modules, string introduction, CancellationToken ct, bool contextual = false, IReadOnlyList<ConversationMenuOption>? choices = null)
    {
        if (choices != null)
        {
            var available = await BuildOptions(workspace, context, modules, ct, paginate: false, catalogOnly: true);
            context.RecoveryOptions = choices.Where(choice => available.Any(option => option.Key == choice.Key && option.Kind == choice.Kind)).ToList();
            context.RecoveryOptionsOffset = 0; context.RecoveryHasMore = false;
            context.RecoveryOptionsContext = Fingerprint(context);
            var list = string.Join("\n", context.RecoveryOptions.Select((choice, i) => $"{i + 1}. {choice.Label}"));
            return await SaveAsync(workspace, phone, source, context, new(introduction + "\n" + list, Expected: ExpectedReply.Selection), ct);
        }
        ConversationDiagnostics.Count("Clarification", context.ClarificationAttempts > 0 ? "Repeated" : "First");
        if (++context.ClarificationAttempts >= Limits.MaxClarifications)
            return await SaveAsync(workspace, phone, source, context, new(Interpretation: new() { Intent = ConversationIntent.Support }), ct);
        if (contextual)
        {
            // A semantic clarification is one question, not an unsolicited global menu.
            context.RecoveryOptions.Clear(); context.RecoveryOptionsContext = null;
            return await SaveAsync(workspace, phone, source, context, new(introduction), ct);
        }
        return await SaveAsync(workspace, phone, source, context, new(await PrepareOptionsAsync(workspace, context, modules, introduction, ct), Expected: ExpectedReply.Selection), ct);
    }

    public async Task<string> DecorateAsync(Guid workspace, string phone, ConversationContextDto context, HashSet<string> modules, string response, CancellationToken ct, IReadOnlyList<ConversationMenuOption>? verifiedChoices = null)
    {
        var decorated = await PrepareOptionsAsync(workspace, context, modules, response, ct, replacePicker: true, verifiedChoices: verifiedChoices);
        await contexts.SaveContextAsync(workspace, phone, context, ct);
        return decorated;
    }

    private async Task<string> PrepareOptionsAsync(Guid workspace, ConversationContextDto context, HashSet<string> modules, string response, CancellationToken ct, bool replacePicker = false, IReadOnlyList<ConversationMenuOption>? verifiedChoices = null)
    {
        if (context.RecoveryOptionsContext != Fingerprint(context)) context.RecoveryOptionsOffset = 0;
        var available = verifiedChoices?.ToList() ?? await BuildOptions(workspace, context, modules, ct, paginate: false);
        context.RecoveryHasMore = context.RecoveryOptionsOffset + 5 < available.Count;
        context.RecoveryOptions = available.Skip(context.RecoveryOptionsOffset).Take(5).ToList();
        context.RecoveryOptionsContext = Fingerprint(context);
        var noOptions = context.CurrentStep is "COLLECT_CUSTOMERNAME" or "COLLECT_DATE" or "CONFIRM" ? Continuation(context)
            : "No hay opciones vigentes para este paso. Puedes indicar otra sede o fecha, o escribir asesor para solicitar ayuda humana.";
        if (context.RecoveryOptions.Count == 0) return response + "\n" + noOptions;
        var menu = string.Join("\n", context.RecoveryOptions.Select((o, i) => $"{i + 1}. {o.Label}"))
                + (context.RecoveryHasMore ? "\nHay más opciones disponibles: escribe ver más. Para solicitar ayuda humana escribe asesor." : "")
                + (context.RecoveryOptionsOffset > 0 ? "\nPara volver al inicio escribe menú." : "");
        return replacePicker ? ComposePickerResponse(response, available, menu)
            : response + "\n\n" + menu + "\n\n" + SecretaryPresentation.ChooseOption;
    }

    private static string ComposePickerResponse(string response, List<ConversationMenuOption> available, string menu)
    {
        // Only replace exact, authoritative option lines. Other lists, explanations
        // and questions belong to the response and must survive the composition.
        var pickerLines = available.Select(o => "• " + o.Label).ToHashSet(StringComparer.Ordinal);
        var lines = response.Replace("\r\n", "\n").Split('\n');
        var finalLine = Array.FindLastIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        var hasQuestion = response.Contains('¿') && response.Contains('?');
        var insertion = Array.FindIndex(lines, line => pickerLines.Contains(line.Trim()));
        if (insertion < 0) insertion = hasQuestion ? finalLine : lines.Length;
        var parts = new List<string>();
        for (var i = 0; i <= lines.Length; i++)
        {
            if (i == insertion) parts.Add(menu);
            if (i < lines.Length && !pickerLines.Contains(lines[i].Trim())) parts.Add(lines[i]);
        }
        if (!hasQuestion) parts.Add(SecretaryPresentation.ChooseOption);
        return string.Join("\n", parts);
    }

    private async Task<List<ConversationMenuOption>> BuildOptions(Guid workspace, ConversationContextDto context, HashSet<string> modules, CancellationToken ct, bool paginate = true, bool catalogOnly = false)
    {
        var result = new List<ConversationMenuOption>();
        if (!catalogOnly && context.CurrentGoal is "RESERVATION" or "BOOKING" && modules.Contains("RESERVATIONS") && modules.Contains("SERVICES"))
        {
            if (context.CurrentStep == "COLLECT_LOCATION") result.AddRange((await locations.GetLocationsAsync(workspace, ct)).Where(l => l.Id != null).OrderBy(l => l.Name).ThenBy(l => l.Id).Select(l => new ConversationMenuOption(l.Id!, l.Name, "Location")));
            else if (context.CurrentStep == "COLLECT_SERVICE" && context.SelectedLocationId != null)
                result.AddRange((await offerings.GetServicesAsync(workspace, context.SelectedLocationId, null, ct)).Where(s => s.IsActive && s.RequiresReservation && s.DurationInMinutes >= 5
                    && (string.Equals(s.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.LocationScope, "SPECIFIC", StringComparison.OrdinalIgnoreCase) && s.LocationIds.Contains(context.SelectedLocationId)))
                    .OrderBy(s => s.Name).ThenBy(s => s.Id).Select(s => new ConversationMenuOption(s.Id, s.Name, "Service")));
            else if (context.CurrentStep == "COLLECT_TIME" && context.SelectedLocationId != null && context.SelectedServiceId != null && DateTime.TryParseExact(context.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var profile = await profiles.GetProfileAsync(workspace, ct);
                TimeZoneInfo zone;
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone); }
                catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
                catch (InvalidTimeZoneException) { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
                var available = (await reservations.GetAvailabilityAsync(workspace, context.SelectedLocationId, context.SelectedServiceId, date, ct)).Where(s => s.IsAvailable);
                if (context.PreferredTime != null) available = new TimePreferenceResolver().FilterAvailableSlots(available, DateOnly.FromDateTime(date), zone, context.PreferredTime);
                result.AddRange(available.OrderBy(s => s.StartTime).Select(s => TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone).ToString("HH:mm", CultureInfo.InvariantCulture)).Distinct().Select(t => new ConversationMenuOption(t, t, "Time")));
            }
            else return result;
        }
        else
        {
            var products = modules.Contains("CATALOG") && (await offerings.GetProductsAsync(workspace, null, null, ct)).Any(p => p.IsActive);
            var services = modules.Contains("SERVICES") ? (await offerings.GetServicesAsync(workspace, null, null, ct)).Where(s => s.IsActive).ToList() : new();
            if (products) result.Add(new("ProductQuery", "Consultar productos", "Action"));
            if (services.Count != 0) result.Add(new("ServiceQuery", "Consultar servicios", "Action"));
            if (catalogOnly) return result;
            if (modules.Contains("RESERVATIONS") && services.Any(s => s.RequiresReservation && s.DurationInMinutes >= 5)) result.Add(new("Reservation", "Reservar un servicio", "Action"));
            if (modules.Contains("ORDERS") && products) result.Add(new("Order", "Preparar un pedido", "Action"));
            // Local to this menu construction; never reused across turns or an await
            // that performs a business mutation. No entitlement checks are cached.
            bool? hasLocations = null;
            if (modules.Contains("LOCATIONS"))
            {
                hasLocations = (await locations.GetLocationsAsync(workspace, ct)).Any();
                if (hasLocations.Value) result.Add(new("Location", "Consultar ubicaciones", "Action"));
            }
            if (modules.Contains("BUSINESS_HOURS"))
            {
                var hasHours = (await hours.GetBusinessHoursAsync(workspace, null, ct)).Any();
                if (!hasHours)
                {
                    if (hasLocations.HasValue) ConversationDiagnostics.QueriesAvoided(logger, "MenuLocations", 1);
                    else hasLocations = (await locations.GetLocationsAsync(workspace, ct)).Any();
                }
                if (hasHours || hasLocations == true) result.Add(new("BusinessHours", "Consultar horarios", "Action"));
            }
            if (modules.Contains("REQUESTS")) result.Add(new("Request", "Registrar una solicitud", "Action"));
            result.Add(new("Support", "Hablar con un asesor", "Action"));
        }
        context.RecoveryOptionsOffset = Math.Clamp(context.RecoveryOptionsOffset, 0, result.Count == 0 ? 0 : (result.Count - 1) / 5 * 5);
        context.RecoveryHasMore = context.RecoveryOptionsOffset + 5 < result.Count;
        return paginate ? result.Skip(context.RecoveryOptionsOffset).Take(5).ToList() : result;
    }

    private async Task<RecoveryDecision> SaveAsync(Guid workspace, string phone, string source, ConversationContextDto context, RecoveryDecision decision, CancellationToken ct)
    {
        context.RecoveryLastMessageId = source; context.RecoveryLastResponse = decision.Response;
        context.RecoveryLastInterpretation = decision.Interpretation == null ? null : JsonSerializer.Serialize(decision.Interpretation);
        await contexts.SaveContextAsync(workspace, phone, context, ct); return decision;
    }
}

