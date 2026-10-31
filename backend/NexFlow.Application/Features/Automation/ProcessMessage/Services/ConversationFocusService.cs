using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

// State-only policy. Existing flows revalidate facts and own every business effect.
public sealed partial class ConversationFocusService(IContextRecoveryService contexts, IClock clock, IOptions<AssistantRecoveryOptions> options)
{
    public bool Enabled => options.Value.EnableContextualFocus;
    public static OperationKind? Kind(string? goal) => goal switch {
        "ORDER" => OperationKind.Order, "BOOKING" or "RESERVATION" => OperationKind.Reservation, _ => null
    };
    public FocusChangeProposal? Prepare(Guid workspace, string source, string text, ConversationContextDto context, AiInterpretation semantic)
    {
        if (!Enabled || semantic.RequiresClarification || semantic.Directive != ConversationDirective.None
            || semantic.Act is not (DialogueAct.StartOperation or DialogueAct.Resume)
            || semantic.Items.Count != 0 || semantic.InformationalIntent != null) return null;
        var target = semantic.Intent switch {
            ConversationIntent.Order => OperationKind.Order, ConversationIntent.Reservation => OperationKind.Reservation, _ => (OperationKind?)null
        };
        var evidence = semantic.Evidence.Where(e => e.Slot == "Act").ToArray();
        if (target == null || Kind(context.CurrentGoal) == target || evidence.Length != 1
            || evidence[0].NormalizedValue != semantic.Act.ToString() || string.IsNullOrWhiteSpace(evidence[0].Text)
            || evidence[0].Text.Length > 300 || !AiEvidenceValidator.HasFragment(text, evidence[0].Text)) return null;
        if (Kind(context.CurrentGoal) == null && context.Focus?.Suspended?.Identity.Kind != target) return null;
        return new(workspace, source, context.StateVersion, target.Value, semantic.Act == DialogueAct.Resume);
    }

    public FocusChangeProposal? Replay(Guid workspace, string source, ConversationContextDto context)
        => Enabled && context.Focus is { Version: 1, Active: not null } focus && focus.WorkspaceId == workspace
            && focus.LastTransitionSource == source && Kind(context.CurrentGoal) == focus.Active.Kind
                ? new(workspace, source, context.StateVersion, focus.Active.Kind, false) : null;

    public async Task<string?> ApplyAsync(Guid workspace, string phone, string conversation, string source,
        ConversationContextDto state, FocusChangeProposal proposal, HashSet<string> modules, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversation);
        if (!Enabled) return "El cambio de foco no está habilitado; tu borrador sigue guardado.";
        if (state.Mode == "Human" || proposal.WorkspaceId != workspace || proposal.SourceMessageId != source
            || proposal.ExpectedStateVersion != state.StateVersion) throw new ConcurrencyException("Stale or foreign focus proposal.");
        var focus = state.Focus;
        if (focus != null && (focus.Version != 1 || focus.WorkspaceId != workspace || focus.ConversationId != conversation
            || focus.Active is not { Id: var id, ActivationRevision: > 0 } || id == Guid.Empty || !Enum.IsDefined(focus.Active.Kind)
            || focus.Suspended is { } saved && (!Enum.IsDefined(saved.Identity.Kind) || saved.Identity.Kind == focus.Active.Kind || saved.Identity.Id == id)))
            throw new ConcurrencyException("Unsupported or foreign focus identity.");
        var intent = proposal.Target == OperationKind.Order ? ConversationIntent.Order : ConversationIntent.Reservation;
        if (AiIntentAccess.GetDeniedResponse(intent, modules) is { } denied) return denied;
        if (state.PendingRecoveryAction != null || focus?.PendingDiscard != null || state.ReservationManagement != null
            || state.ReservationCommitSourceMessageId != null || state.CurrentStep?.StartsWith("ORDER_COMMIT", StringComparison.Ordinal) == true)
            return "Primero debemos resolver la decisión o recuperación pendiente. No he cambiado ni descartado ningún borrador.";
        if (focus?.LastTransitionSource == source && Kind(state.CurrentGoal) == proposal.Target) return null;
        var suspended = focus?.Suspended;
        var resuming = suspended?.Identity.Kind == proposal.Target;
        if (proposal.ResumeOnly && !resuming) return "No hay un borrador de ese tipo guardado para retomar. Tu preparación actual sigue intacta.";
        if (suspended != null && (!resuming || suspended.Identity.Id == Guid.Empty || suspended.Identity.ActivationRevision < 1))
            return "Ya hay un borrador suspendido. No crearé otro ni sustituiré el que tienes guardado.";
        if (resuming && (suspended!.Identity.ExpiresAt <= clock.UtcNow || suspended.SuspendedAt > clock.UtcNow))
            return "El borrador guardado ha vencido y no lo he reactivado. Tu preparación actual sigue intacta; puedes solicitar un asesor.";

        var activeKind = Kind(state.CurrentGoal);
        if (activeKind == proposal.Target) return "Esa preparación ya está en foco. No he creado otro borrador.";
        if (state.CurrentGoal != null && activeKind == null) return "Completa la operación actual antes de cambiar el foco.";
        var expires = state.LastUpdated.AddHours(options.Value.ContextExpiryHours);
        var active = focus != null && focus.Active.Kind == activeKind ? focus.Active
            : activeKind is { } current ? new OperationIdentity(Guid.NewGuid(), current, 1, expires) : null;
        if (active != null && active.ExpiresAt <= clock.UtcNow)
            return "La preparación actual requiere revisión por antigüedad. No he cambiado el foco ni descartado datos.";
        var paused = active == null ? null : Capture(state, active);
        if (paused != null && (paused.Items.Count > 10 || Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(paused)) > 16 * 1024))
            return "El borrador supera el límite seguro para pausarlo. Lo he conservado; puedes continuar con él o solicitar un asesor.";
        var next = resuming ? suspended!.Identity with { ActivationRevision = checked(suspended.Identity.ActivationRevision + 1) }
            : new OperationIdentity(Guid.NewGuid(), proposal.Target, 1, clock.UtcNow.AddHours(options.Value.ContextExpiryHours));
        ClearProjection(state);
        state.CurrentGoal = proposal.Target == OperationKind.Order ? "ORDER" : "RESERVATION";
        if (resuming) Restore(state, suspended!);
        state.CurrentStep = proposal.Target == OperationKind.Order ? resuming && state.OrderDraftItems.Count > 0 ? "ORDER_REVIEW:" : "ORDER_COLLECT_ITEMS" : DialoguePolicy.NextBookingStep(state);
        state.Focus = new() { WorkspaceId = workspace, ConversationId = conversation, Active = next, Suspended = paused,
            LastTransitionSource = source, LastTransitionResumed = resuming };
        await contexts.SaveContextAsync(workspace, phone, state, ct);
        ConversationDiagnostics.Count("FocusTransition", resuming ? "Resume" : "Start");
        return null;
    }

    private SuspendedConversationDraft Capture(ConversationContextDto state, OperationIdentity identity) => identity.Kind == OperationKind.Order
        ? new(identity, clock.UtcNow, state.SelectedLocationId, state.OrderDraftItems.Select(i => new OrderDraftItem {
            ProductId = i.ProductId, ProductName = i.ProductName, Quantity = i.Quantity }).ToArray())
        : new(identity, clock.UtcNow, state.SelectedLocationId, [], state.SelectedServiceId, state.TargetDate, state.TargetTime,
            state.RealCustomerName, state.PendingCustomerName, state.PendingServiceIds.ToArray(), state.PendingDialogueSlots.ToArray(), state.PreferredTime);

    private static void Restore(ConversationContextDto state, SuspendedConversationDraft draft)
    {
        state.SelectedLocationId = draft.LocationId;
        if (draft.Identity.Kind == OperationKind.Order)
            state.OrderDraftItems = draft.Items.Select(i => new OrderDraftItem { ProductId = i.ProductId, ProductName = i.ProductName, Quantity = i.Quantity }).ToList();
        else {
            state.SelectedServiceId = draft.ServiceId; state.TargetDate = draft.Date; state.TargetTime = draft.Time;
            state.RealCustomerName = draft.CustomerName; state.PendingCustomerName = draft.PendingCustomerName;
            state.PendingServiceIds = (draft.PendingServiceIds ?? []).ToList(); state.PendingDialogueSlots = (draft.PendingSlots ?? []).ToList();
            state.PreferredTime = draft.PreferredTime;
        }
    }
    private static void ClearProjection(ConversationContextDto state)
    {
        state.CurrentQuestion = null; state.LastQuestion = null; state.LastIntent = null; state.Information = null;
        state.RecoveryOptions.Clear(); state.RecoveryOptionsContext = null; state.RecoveryOptionsOffset = 0; state.RecoveryHasMore = false;
        state.ClarificationAttempts = 0; state.RecoveryLastMessageId = null; state.RecoveryLastResponse = null; state.RecoveryLastInterpretation = null;
        state.PendingReferenceExpiresAt = null; state.SelectedLocationId = null; state.SelectedServiceId = null;
        state.TargetDate = null; state.TargetTime = null; state.RealCustomerName = null; state.PendingCustomerName = null;
        state.PendingServiceIds.Clear(); state.PendingDialogueSlots.Clear(); state.PreferredTime = null; state.ReservationServiceOffset = 0;
        state.OrderDraftItems.Clear(); state.MissingFields.Clear();
    }
}
