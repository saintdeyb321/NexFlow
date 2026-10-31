using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public sealed partial class ConversationFocusService
{
    // These are three published protocol choices, not a natural-language grammar.
    public static string DiscardChoice(DraftDiscardScope scope) => scope switch {
        DraftDiscardScope.Active => "Descartar activo", DraftDiscardScope.Suspended => "Descartar suspendido", _ => "Descartar ambos"
    };
    private static string DiscardFingerprint(ConversationContextDto state) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(ConversationQuestion.Fingerprint(state) + JsonSerializer.Serialize(state.Focus?.Suspended))));

    public string BeginDiscard(Guid workspace, string conversation, string source, ConversationContextDto state, DraftDiscardScope? scope)
    {
        var focus = state.Focus;
        if (!Enabled || focus == null) return "No hay una preparación suspendida que pueda descartar. No he cambiado ningún dato.";
        if (state.Mode == "Human" || focus.WorkspaceId != workspace || focus.ConversationId != conversation || focus.Version != 1)
            throw new ConcurrencyException("Foreign discard scope or human control.");
        if (state.ReservationManagement != null || state.ReservationCommitSourceMessageId != null
            || state.CurrentStep?.StartsWith("ORDER_COMMIT", StringComparison.Ordinal) == true
            || state.PendingRecoveryAction is not (null or "CANCEL"))
            return "Primero debemos resolver la recuperación pendiente. No he descartado nada.";
        if (scope == DraftDiscardScope.Active && state.CurrentGoal == null)
            return "No hay un borrador activo. Puedes retomar el guardado o elegir «Descartar suspendido».";
        if (scope == DraftDiscardScope.Suspended && focus.Suspended == null)
            return "No hay un borrador suspendido. La preparación activa sigue intacta.";
        state.PendingRecoveryAction = null; state.CurrentQuestion = null;
        focus.PendingDiscard = new(scope, source, clock.UtcNow.AddMinutes(10), DiscardFingerprint(state));
        return scope == null ? DiscardScopeQuestion : DiscardConfirmation(scope.Value);
    }

    private const string DiscardScopeQuestion = "¿Qué preparación quieres descartar? Puedes elegir «Descartar activo», «Descartar suspendido» o «Descartar ambos». No se cancela ningún pedido ni reserva ya registrado.";
    private static string DiscardConfirmation(DraftDiscardScope scope) => scope switch {
        DraftDiscardScope.Active => "¿Confirmas que descartemos solo el borrador activo? El suspendido seguirá guardado. Responde «Sí» o «No».",
        DraftDiscardScope.Suspended => "¿Confirmas que descartemos solo el borrador suspendido, aunque haya vencido? La preparación activa no cambiará. Responde «Sí» o «No».",
        _ => "¿Confirmas que descartemos ambos borradores en preparación? Esto no cancela operaciones registradas. Responde «Sí» o «No»."
    };

    public string? HandleDiscard(Guid workspace, string conversation, string source, string text, ConversationContextDto state)
    {
        if (!Enabled || state.Focus == null) return null;
        if (state.Mode == "Human" || state.Focus.WorkspaceId != workspace || state.Focus.ConversationId != conversation || state.Focus.Version != 1)
            throw new ConcurrencyException("Foreign discard scope or human control.");
        if (state.Focus.PendingDiscard is { } replay && replay.SourceMessageId == source
            && state.CurrentQuestion?.QuestionId == replay.QuestionId)
            return replay.Scope is { } scope ? DiscardConfirmation(scope) : DiscardScopeQuestion;
        var normalized = ConversationRecoveryService.Normalize(text);
        var choice = Enum.GetValues<DraftDiscardScope>().Cast<DraftDiscardScope?>()
            .FirstOrDefault(s => ConversationRecoveryService.Normalize(DiscardChoice(s!.Value)) == normalized);
        if (choice != null) return BeginDiscard(workspace, conversation, source, state, choice);
        if (state.Focus.PendingDiscard is not { } pending) return null;
        if (!ConversationConfirmation.IsAffirmative(text) && !ConversationConfirmation.IsNegative(text)) return null;
        if (state.Mode == "Human" || state.Focus.WorkspaceId != workspace || state.Focus.ConversationId != conversation)
            throw new ConcurrencyException("Foreign discard answer.");
        if (pending.ExpiresAt <= clock.UtcNow || pending.Fingerprint != DiscardFingerprint(state)
            || state.CurrentQuestion is not { Expected: ExpectedReply.DiscardDrafts } question || question.QuestionId != pending.QuestionId
            || question.ConversationId != conversation || question.WorkspaceId != workspace || question.SourceMessageId != pending.SourceMessageId
            || question.OperationId != state.Focus.Active.Id || question.ActivationRevision != state.Focus.Active.ActivationRevision
            || question.DraftFingerprint != ConversationQuestion.Fingerprint(state) || question.IssuedAt > clock.UtcNow || question.ExpiresAt <= clock.UtcNow)
            return BeginDiscard(workspace, conversation, source, state, null);
        if (ConversationConfirmation.IsNegative(text)) {
            state.Focus.PendingDiscard = null; state.CurrentQuestion = null;
            return "No he descartado ninguna preparación. Puedes continuar o retomar el otro borrador.";
        }
        if (pending.Scope == null || pending.SourceMessageId == source) return DiscardScopeQuestion;
        if (pending.Scope is DraftDiscardScope.Active or DraftDiscardScope.Both) {
            ClearProjection(state); state.CurrentGoal = null; state.CurrentStep = null;
        }
        if (pending.Scope is DraftDiscardScope.Suspended or DraftDiscardScope.Both) state.Focus.Suspended = null;
        state.Focus.PendingDiscard = null; state.Focus.LastTransitionSource = null; state.CurrentQuestion = null;
        if (state.CurrentGoal == null && state.Focus.Suspended == null) state.Focus = null;
        var response = pending.Scope switch {
            DraftDiscardScope.Active => "He descartado solo el borrador activo. El suspendido sigue guardado para retomarlo.",
            DraftDiscardScope.Suspended => "He descartado solo el borrador suspendido. Puedes continuar con la preparación activa o iniciar una nueva si ya terminó.",
            _ => "He descartado ambos borradores. Puedes iniciar una nueva preparación. Ninguna operación registrada fue cancelada."
        };
        state.RecoveryLastMessageId = source; state.RecoveryLastResponse = response; state.RecoveryLastInterpretation = null;
        return response;
    }
}
