using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Dialogue.Contracts;

public enum ExpectedReply { Information, Selection, RequestedData, StartReservation, ConfirmReservation, RecoveryDecision, ConfirmOrder, SelectReservationTime, EditReservationField, DiscardDrafts }

// Backend-owned reference in the existing CAS envelope, not consent or a business command.
public sealed record ConversationQuestion(string QuestionId, Guid WorkspaceId, string SourceMessageId,
    ExpectedReply Expected, ConversationIntent Intent, string DraftFingerprint, string? OptionsFingerprint,
    DateTime IssuedAt, DateTime ExpiresAt, string? ConversationId = null, Guid? OperationId = null, long? ActivationRevision = null)
{
    public static string Fingerprint(ConversationContextDto state)
    {
        var draft = Hash(new {
        state.CurrentGoal, state.CurrentStep, state.SelectedLocationId, state.SelectedServiceId,
        state.TargetDate, state.TargetTime, state.PreferredTime, state.RealCustomerName,
        state.PendingCustomerName, state.PendingServiceIds, state.PendingDialogueSlots,
        state.OrderDraftItems, state.PendingRecoveryAction
        });
        // Byte-identical legacy fingerprint when CTA is absent.
        return state.Focus?.Active is { } active ? Hash(new { Draft = draft, active.Id, active.ActivationRevision }) : draft;
    }
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    public static ConversationQuestion? Current(Guid workspace, ConversationContextDto state, DateTime now)
        => state.CurrentQuestion is { } question && state.Mode != "Human" && question.WorkspaceId == workspace
            && !string.IsNullOrWhiteSpace(question.QuestionId) && !string.IsNullOrWhiteSpace(question.SourceMessageId)
            && question.IssuedAt <= now && question.ExpiresAt > now && Enum.IsDefined(question.Expected) && Enum.IsDefined(question.Intent)
            && question.DraftFingerprint == Fingerprint(state)
            && (state.Focus == null ? question.OperationId == null && question.ActivationRevision == null
                : state.Focus.WorkspaceId == workspace && state.Focus.Version == 1 && state.Focus.Active is { } active
                    && active.ExpiresAt > now
                    && (ConversationFocusKind(state.CurrentGoal) is not { } kind || active.Kind == kind)
                    && question.ConversationId == state.Focus.ConversationId && question.OperationId == active.Id
                    && question.ActivationRevision == active.ActivationRevision)
            && (question.Expected != ExpectedReply.Selection || question.OptionsFingerprint == Hash(state.RecoveryOptions))
            ? question : null;

    private static OperationKind? ConversationFocusKind(string? goal) => goal switch {
        "ORDER" => OperationKind.Order, "BOOKING" or "RESERVATION" => OperationKind.Reservation, _ => null
    };

    public static void Remember(Guid workspace, string source, ConversationContextDto state, ExpectedReply expected,
        ConversationIntent intent, string response, DateTime now, string? conversationId = null)
    {
        // OrderFlow owns its existing continuation question; do not replace it with a quote or factual interruption.
        if (state.CurrentGoal != "ORDER") state.LastQuestion = response;
        state.CurrentQuestion = new(Guid.NewGuid().ToString("N"), workspace, source, expected, intent,
            Fingerprint(state), expected == ExpectedReply.Selection ? Hash(state.RecoveryOptions) : null,
            now, now.AddMinutes(10), conversationId, state.Focus?.Active.Id, state.Focus?.Active.ActivationRevision);
    }

    public static bool HasCurrentOrderReview(Guid workspace, string conversation, ConversationContextDto state,
        string quoteFingerprint, DateTime now)
        => state.CurrentGoal == "ORDER" && state.CurrentStep == "ORDER_CONFIRM:" + quoteFingerprint
            && state.PendingRecoveryAction == null
            && Current(workspace, state, now) is { Expected: ExpectedReply.ConfirmOrder, Intent: ConversationIntent.Order } question
            && question.ConversationId == conversation;

    public static bool CanConfirmReservation(Guid workspace, ConversationContextDto state, string text, DateTime now)
        => state.CurrentGoal is "BOOKING" or "RESERVATION" && state.CurrentStep == "CONFIRM"
            && state.PendingRecoveryAction == null && ConversationConfirmation.IsAffirmative(text)
            && Current(workspace, state, now) is { Expected: ExpectedReply.ConfirmReservation, Intent: ConversationIntent.Reservation };

    // Only a linguistic projection: no customer name, full answer, history, summary or commercial facts.
    public string LinguisticQuestion(ConversationContextDto state) => Expected switch {
        ExpectedReply.StartReservation => "¿Quieres que te ayude a empezar una reserva?",
        ExpectedReply.ConfirmReservation => "¿Confirmas el resumen vigente de la reserva?",
        ExpectedReply.ConfirmOrder => "¿Confirmas el resumen vigente de la solicitud de pedido?",
        ExpectedReply.SelectReservationTime => "¿Deseas elegir la fecha y hora que acabamos de comprobar? Esto no confirma la reserva.",
        ExpectedReply.EditReservationField => "¿Qué dato de tu reserva en preparación deseas editar: sede, servicio, fecha, hora o nombre? La respuesta señala el campo a editar, no una consulta de dirección ni consentimiento.",
        ExpectedReply.Selection => "Elige una opción de la lista vigente.",
        ExpectedReply.RecoveryDecision => "Confirma o rechaza la decisión pendiente sobre el borrador.",
        ExpectedReply.DiscardDrafts => "Decide el alcance del descarte de borradores; no autoriza ninguna transacción.",
        ExpectedReply.RequestedData => state.CurrentStep switch {
            "COLLECT_LOCATION" => "¿En qué sede deseas reservar?", "COLLECT_SERVICE" => "¿Qué servicio deseas reservar?",
            "COLLECT_DATE" => "¿Para qué fecha?", "COLLECT_TIME" => "¿Qué hora prefieres?",
            "ORDER_COLLECT_ITEMS" => "¿Qué productos y cantidades deseas solicitar?",
            "COLLECT_CUSTOMERNAME" => "¿Cuál es tu nombre y apellido?", _ => "¿Qué dato deseas aportar al borrador?" },
        _ => "¿Qué dato necesitas precisar en la consulta informativa?"
    };
}

