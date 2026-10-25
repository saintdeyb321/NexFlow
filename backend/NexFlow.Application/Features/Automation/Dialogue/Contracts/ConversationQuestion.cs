using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Dialogue.Contracts;

public enum ExpectedReply { Information, Selection, RequestedData, StartReservation, ConfirmReservation, RecoveryDecision, ConfirmOrder }

// Backend-owned reference in the existing CAS envelope, not consent or a business command.
public sealed record ConversationQuestion(string QuestionId, Guid WorkspaceId, string SourceMessageId,
    ExpectedReply Expected, ConversationIntent Intent, string DraftFingerprint, string? OptionsFingerprint,
    DateTime IssuedAt, DateTime ExpiresAt)
{
    public static string Fingerprint(ConversationContextDto state) => Hash(new {
        state.CurrentGoal, state.CurrentStep, state.SelectedLocationId, state.SelectedServiceId,
        state.TargetDate, state.TargetTime, state.PreferredTime, state.RealCustomerName,
        state.PendingCustomerName, state.PendingServiceIds, state.PendingDialogueSlots,
        state.OrderDraftItems, state.PendingRecoveryAction
    });
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    public static ConversationQuestion? Current(Guid workspace, ConversationContextDto state, DateTime now)
        => state.CurrentQuestion is { } question && state.Mode != "Human" && question.WorkspaceId == workspace
            && !string.IsNullOrWhiteSpace(question.QuestionId) && !string.IsNullOrWhiteSpace(question.SourceMessageId)
            && question.IssuedAt <= now && question.ExpiresAt > now && Enum.IsDefined(question.Expected) && Enum.IsDefined(question.Intent)
            && question.DraftFingerprint == Fingerprint(state)
            && (question.Expected != ExpectedReply.Selection || question.OptionsFingerprint == Hash(state.RecoveryOptions))
            ? question : null;

    public static void Remember(Guid workspace, string source, ConversationContextDto state, ExpectedReply expected,
        ConversationIntent intent, string response, DateTime now)
    {
        // OrderFlow owns its existing continuation question; do not replace it with a quote or factual interruption.
        if (state.CurrentGoal != "ORDER") state.LastQuestion = response;
        state.CurrentQuestion = new(Guid.NewGuid().ToString("N"), workspace, source, expected, intent,
            Fingerprint(state), expected == ExpectedReply.Selection ? Hash(state.RecoveryOptions) : null,
            now, now.AddMinutes(10));
    }

    public static bool CanConfirmReservation(Guid workspace, ConversationContextDto state, string text, DateTime now)
        => state.CurrentGoal is "BOOKING" or "RESERVATION" && state.CurrentStep == "CONFIRM"
            && state.PendingRecoveryAction == null && ConversationConfirmation.IsAffirmative(text)
            && Current(workspace, state, now) is { Expected: ExpectedReply.ConfirmReservation, Intent: ConversationIntent.Reservation };

    // Only a linguistic projection: no customer name, full answer, history, summary or commercial facts.
    public string LinguisticQuestion(ConversationContextDto state) => Expected switch {
        ExpectedReply.StartReservation => "¿Quieres que te ayude a empezar una reserva?",
        ExpectedReply.ConfirmReservation => "¿Confirmas el resumen vigente de la reserva?",
        ExpectedReply.ConfirmOrder => "¿Confirmas el resumen vigente de la solicitud de pedido?",
        ExpectedReply.Selection => "Elige una opción de la lista vigente.",
        ExpectedReply.RecoveryDecision => "Confirma o rechaza la decisión pendiente sobre el borrador.",
        ExpectedReply.RequestedData => state.CurrentStep switch {
            "COLLECT_LOCATION" => "¿En qué sede deseas reservar?", "COLLECT_SERVICE" => "¿Qué servicio deseas reservar?",
            "COLLECT_DATE" => "¿Para qué fecha?", "COLLECT_TIME" => "¿Qué hora prefieres?",
            "ORDER_COLLECT_ITEMS" => "¿Qué productos y cantidades deseas solicitar?",
            "COLLECT_CUSTOMERNAME" => "¿Cuál es tu nombre y apellido?", _ => "¿Qué dato deseas aportar al borrador?" },
        _ => "¿Qué dato necesitas precisar en la consulta informativa?"
    };
}

